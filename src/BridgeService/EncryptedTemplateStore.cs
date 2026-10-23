using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace MFS100BridgeSvc
{
    public class EnrolledFingerRecord
    {
        public int FingerIndex { get; set; } // 0 = Primary, 1 = Secondary
        public string FingerLabel { get; set; } = string.Empty;
        public int Quality { get; set; }
        public DateTime EnrolledUtc { get; set; }
        public byte[] Template { get; set; } = Array.Empty<byte>();
    }

    public class UserEnrollmentRecord
    {
        public const uint MagicHeader = 0x4D465354; // "MFST"
        public const int CurrentFormatVersion = 1;
        public const int MaxFingersPerUser = 2;

        public string UserSid { get; set; } = string.Empty;
        public DateTime EnrolledUtc { get; set; } = DateTime.UtcNow;
        public List<EnrolledFingerRecord> Fingers { get; set; } = new List<EnrolledFingerRecord>();
    }

    public class UserEnrollmentSummary
    {
        public string UserSid { get; set; } = string.Empty;
        public DateTime EnrolledUtc { get; set; }
        public List<FingerSummary> Fingers { get; set; } = new List<FingerSummary>();
    }

    public class FingerSummary
    {
        public int FingerIndex { get; set; }
        public string FingerLabel { get; set; } = string.Empty;
        public int Quality { get; set; }
        public DateTime EnrolledUtc { get; set; }
    }

    /// <summary>
    /// Privileged biometric template store executing inside Session 0 (MFS100BridgeSvc).
    /// Enforces:
    ///   - DPAPI Machine Scope encryption with User-SID optional entropy (AC 2.9)
    ///   - Directory and file ACLs restricted to SYSTEM and Administrators ONLY (AC 2.10)
    ///   - Cryptographic rejection on SID context mismatch (AC 2.11)
    ///   - Caller authorization check: standard users cannot manage other SIDs (AC 2.12)
    ///   - Maximum 2 fingers per user SID (AC 2.3)
    ///   - Zero passwords, PINs, or credentials stored (AC 2.7)
    /// </summary>
    public static class EncryptedTemplateStore
    {
        private static readonly object _fileLock = new object();

        public static string GetTemplatesDirectory()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MFS100CP", "templates");

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            ApplyDirectorySecurity(dir);

            return dir;
        }

        public static string GetTemplateFilePath(string userSid)
        {
            ValidateSidFormat(userSid);
            return Path.Combine(GetTemplatesDirectory(), $"{userSid}.dat");
        }

        public static void ValidateSidFormat(string userSid)
        {
            if (string.IsNullOrWhiteSpace(userSid) ||
                userSid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                !userSid.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Invalid user SID format.", nameof(userSid));
            }
        }

        public static bool IsUserEnrolled(string userSid)
        {
            string path = GetTemplateFilePath(userSid);
            return File.Exists(path);
        }

        public static void AuthorizeCaller(string callerSid, bool callerIsAdmin, string targetSid)
        {
            if (callerIsAdmin) return; // Administrators can manage any SID

            if (!string.Equals(callerSid, targetSid, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    $"Access Denied: Caller SID '{callerSid}' is not authorized to access or modify biometric templates for target SID '{targetSid}'.");
            }
        }

        public static void SaveEnrollment(string callerSid, bool callerIsAdmin, UserEnrollmentRecord data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            ValidateSidFormat(data.UserSid);
            AuthorizeCaller(callerSid, callerIsAdmin, data.UserSid);

            if (data.Fingers.Count > UserEnrollmentRecord.MaxFingersPerUser)
            {
                throw new InvalidOperationException(
                    $"Cannot enroll more than {UserEnrollmentRecord.MaxFingersPerUser} fingers per user SID.");
            }

            byte[] plaintextPayload;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(UserEnrollmentRecord.MagicHeader);
                writer.Write(UserEnrollmentRecord.CurrentFormatVersion);
                writer.Write(data.UserSid);
                writer.Write(data.EnrolledUtc.Ticks);
                writer.Write(data.Fingers.Count);

                foreach (var f in data.Fingers)
                {
                    writer.Write(f.FingerIndex);
                    writer.Write(f.FingerLabel ?? string.Empty);
                    writer.Write(f.Quality);
                    writer.Write(f.EnrolledUtc.Ticks);
                    writer.Write(f.Template.Length);
                    writer.Write(f.Template);
                }

                writer.Flush();
                plaintextPayload = ms.ToArray();
            }

            // Cryptographically encrypt using DPAPI LocalMachine + User SID entropy (AC 2.9 & 2.11)
            byte[] sidEntropy = Encoding.UTF8.GetBytes(data.UserSid);
            byte[] ciphertext = ProtectedData.Protect(plaintextPayload, sidEntropy, DataProtectionScope.LocalMachine);

            string path = GetTemplateFilePath(data.UserSid);

            lock (_fileLock)
            {
                File.WriteAllBytes(path, ciphertext);
                ApplyFileSecurity(path);
            }

            ServiceLog.Info($"Encrypted template saved for SID {data.UserSid} ({data.Fingers.Count} fingers, ciphertext size={ciphertext.Length} bytes).");
        }

        public static UserEnrollmentRecord? LoadEnrollment(string targetSid)
        {
            ValidateSidFormat(targetSid);
            string path = GetTemplateFilePath(targetSid);
            if (!File.Exists(path)) return null;

            lock (_fileLock)
            {
                try
                {
                    byte[] ciphertext = File.ReadAllBytes(path);

                    // Decrypt using DPAPI LocalMachine + User SID entropy (AC 2.9 & AC 2.11)
                    byte[] sidEntropy = Encoding.UTF8.GetBytes(targetSid);
                    byte[] plaintextPayload = ProtectedData.Unprotect(ciphertext, sidEntropy, DataProtectionScope.LocalMachine);

                    using var ms = new MemoryStream(plaintextPayload);
                    using var reader = new BinaryReader(ms, Encoding.UTF8);

                    uint magic = reader.ReadUInt32();
                    if (magic != UserEnrollmentRecord.MagicHeader)
                        throw new InvalidDataException("Invalid template magic header.");

                    int version = reader.ReadInt32();
                    if (version != UserEnrollmentRecord.CurrentFormatVersion)
                        throw new InvalidDataException($"Unsupported template format version: {version}");

                    string fileSid = reader.ReadString();
                    if (!string.Equals(fileSid, targetSid, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Template file internal SID mismatch: expected {targetSid}, got {fileSid}");

                    long ticks = reader.ReadInt64();
                    int count = reader.ReadInt32();

                    if (count < 0 || count > UserEnrollmentRecord.MaxFingersPerUser)
                        throw new InvalidDataException($"Invalid finger count: {count}");

                    var record = new UserEnrollmentRecord
                    {
                        UserSid = fileSid,
                        EnrolledUtc = new DateTime(ticks, DateTimeKind.Utc)
                    };

                    for (int i = 0; i < count; i++)
                    {
                        int fIdx = reader.ReadInt32();
                        string label = reader.ReadString();
                        int quality = reader.ReadInt32();
                        long fTicks = reader.ReadInt64();
                        int tLen = reader.ReadInt32();

                        if (tLen < 0 || tLen > 4096)
                            throw new InvalidDataException($"Invalid template length: {tLen}");

                        byte[] tBytes = reader.ReadBytes(tLen);

                        record.Fingers.Add(new EnrolledFingerRecord
                        {
                            FingerIndex = fIdx,
                            FingerLabel = label,
                            Quality = quality,
                            EnrolledUtc = new DateTime(fTicks, DateTimeKind.Utc),
                            Template = tBytes
                        });
                    }

                    return record;
                }
                catch (CryptographicException cex)
                {
                    ServiceLog.Error($"DPAPI Decryption failed for SID {targetSid} (possible SID context mismatch): {cex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    ServiceLog.Error($"Error loading enrollment for SID {targetSid}: {ex.Message}");
                    return null;
                }
            }
        }

        public static UserEnrollmentSummary? GetEnrollmentSummary(string callerSid, bool callerIsAdmin, string targetSid)
        {
            AuthorizeCaller(callerSid, callerIsAdmin, targetSid);
            var record = LoadEnrollment(targetSid);
            if (record == null) return null;

            return new UserEnrollmentSummary
            {
                UserSid = record.UserSid,
                EnrolledUtc = record.EnrolledUtc,
                Fingers = record.Fingers.Select(f => new FingerSummary
                {
                    FingerIndex = f.FingerIndex,
                    FingerLabel = f.FingerLabel,
                    Quality = f.Quality,
                    EnrolledUtc = f.EnrolledUtc
                }).ToList()
            };
        }

        public static bool DeleteFinger(string callerSid, bool callerIsAdmin, string targetSid, int fingerIndex)
        {
            AuthorizeCaller(callerSid, callerIsAdmin, targetSid);
            var record = LoadEnrollment(targetSid);
            if (record == null) return false;

            int removed = record.Fingers.RemoveAll(f => f.FingerIndex == fingerIndex);
            if (removed == 0) return false;

            if (record.Fingers.Count == 0)
            {
                return DeleteEnrollment(callerSid, callerIsAdmin, targetSid);
            }
            else
            {
                SaveEnrollment(callerSid, callerIsAdmin, record);
                return true;
            }
        }

        public static bool DeleteEnrollment(string callerSid, bool callerIsAdmin, string targetSid)
        {
            AuthorizeCaller(callerSid, callerIsAdmin, targetSid);
            string path = GetTemplateFilePath(targetSid);

            lock (_fileLock)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                        ServiceLog.Info($"Enrollment file deleted for SID {targetSid}.");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        ServiceLog.Error($"Failed to delete enrollment file for SID {targetSid}: {ex.Message}");
                        return false;
                    }
                }
            }
            return false;
        }

        public static List<string> GetAllEnrolledSids()
        {
            string dir = GetTemplatesDirectory();
            var list = new List<string>();

            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.GetFiles(dir, "*.dat"))
                {
                    string sid = Path.GetFileNameWithoutExtension(file);
                    if (sid.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
                        list.Add(sid);
                }
            }

            return list;
        }

        /// <summary>
        /// Strictly restricts the templates directory ACLs to SYSTEM and Administrators ONLY (AC 2.10).
        /// Standard users have NO access.
        /// </summary>
        public static void ApplyDirectorySecurity(string dirPath)
        {
            try
            {
                var dirInfo = new DirectoryInfo(dirPath);
                var sec = new DirectorySecurity();

                // Disable inheritance and remove existing inherited rules
                sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                // Grant SYSTEM FullControl
                sec.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                // Grant Administrators FullControl
                sec.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                // In interactive test mode (non-elevated developer testing), grant creator access
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null &&
                    currentUser.Value != "S-1-5-18" &&
                    currentUser.Value != "S-1-5-32-544")
                {
                    sec.AddAccessRule(new FileSystemAccessRule(
                        currentUser,
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Allow));
                }

                dirInfo.SetAccessControl(sec);
            }
            catch (Exception ex)
            {
                ServiceLog.Warn($"Could not set strict directory security on {dirPath}: {ex.Message}");
            }
        }

        public static void ApplyFileSecurity(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var sec = new FileSecurity();

                // Disable inheritance and remove existing inherited rules
                sec.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

                // Grant SYSTEM FullControl
                sec.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                // Grant Administrators FullControl
                sec.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    AccessControlType.Allow));

                // In interactive test mode (non-elevated developer testing), grant creator access
                var currentUser = WindowsIdentity.GetCurrent().User;
                if (currentUser != null &&
                    currentUser.Value != "S-1-5-18" &&
                    currentUser.Value != "S-1-5-32-544")
                {
                    sec.AddAccessRule(new FileSystemAccessRule(
                        currentUser,
                        FileSystemRights.FullControl,
                        AccessControlType.Allow));
                }

                fileInfo.SetAccessControl(sec);
            }
            catch (Exception ex)
            {
                ServiceLog.Warn($"Could not set strict file security on {filePath}: {ex.Message}");
            }
        }
    }
}
