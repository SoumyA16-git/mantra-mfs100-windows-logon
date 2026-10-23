using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using MFS100Worker;

namespace MFS100Enroll
{
    public class EnrolledFinger
    {
        public int FingerIndex { get; set; } // 0 = Primary, 1 = Secondary
        public string FingerLabel { get; set; } = string.Empty;
        public int Quality { get; set; }
        public byte[] Template { get; set; } = Array.Empty<byte>();
        public DateTime EnrolledUtc { get; set; }
    }

    public class UserEnrollmentData
    {
        public const uint MagicHeader = 0x4D465354; // "MFST"
        public const int CurrentFormatVersion = 1;
        public const int MaxFingersPerUser = 2;

        public string UserSid { get; set; } = string.Empty;
        public DateTime EnrolledUtc { get; set; } = DateTime.UtcNow;
        public List<EnrolledFinger> Fingers { get; set; } = new List<EnrolledFinger>();
    }

    public static class TemplateManager
    {
        private static readonly object _fileLock = new object();

        public static string GetTemplatesDirectory()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MFS100CP", "templates");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string GetTemplateFilePath(string userSid)
        {
            if (string.IsNullOrWhiteSpace(userSid) || userSid.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Invalid user SID format.", nameof(userSid));

            return Path.Combine(GetTemplatesDirectory(), $"{userSid}.dat");
        }

        public static bool IsUserEnrolled(string userSid)
        {
            string path = GetTemplateFilePath(userSid);
            return File.Exists(path);
        }

        public static UserEnrollmentData? LoadEnrollment(string userSid)
        {
            string path = GetTemplateFilePath(userSid);
            if (!File.Exists(path)) return null;

            lock (_fileLock)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using var reader = new BinaryReader(fs, Encoding.UTF8);

                    uint magic = reader.ReadUInt32();
                    if (magic != UserEnrollmentData.MagicHeader)
                        throw new InvalidDataException("Invalid template file magic header.");

                    int version = reader.ReadInt32();
                    if (version != UserEnrollmentData.CurrentFormatVersion)
                        throw new InvalidDataException($"Unsupported template format version: {version}");

                    string sid = reader.ReadString();
                    long ticks = reader.ReadInt64();
                    int fingerCount = reader.ReadInt32();

                    if (fingerCount < 0 || fingerCount > UserEnrollmentData.MaxFingersPerUser)
                        throw new InvalidDataException($"Invalid finger count in template: {fingerCount}");

                    var data = new UserEnrollmentData
                    {
                        UserSid = sid,
                        EnrolledUtc = new DateTime(ticks, DateTimeKind.Utc)
                    };

                    for (int i = 0; i < fingerCount; i++)
                    {
                        int fIdx = reader.ReadInt32();
                        string label = reader.ReadString();
                        int quality = reader.ReadInt32();
                        long fTicks = reader.ReadInt64();
                        int tLen = reader.ReadInt32();

                        if (tLen < 0 || tLen > 4096)
                            throw new InvalidDataException($"Invalid template byte length: {tLen}");

                        byte[] tBytes = reader.ReadBytes(tLen);

                        data.Fingers.Add(new EnrolledFinger
                        {
                            FingerIndex = fIdx,
                            FingerLabel = label,
                            Quality = quality,
                            EnrolledUtc = new DateTime(fTicks, DateTimeKind.Utc),
                            Template = tBytes
                        });
                    }

                    return data;
                }
                catch (Exception ex)
                {
                    WorkerLog.Error($"Error loading enrollment for SID {userSid}: {ex.Message}");
                    return null;
                }
            }
        }

        public static void SaveEnrollment(UserEnrollmentData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(data.UserSid)) throw new ArgumentException("UserSid cannot be empty.");

            // Strict enforcement of maximum 2 fingers per user
            if (data.Fingers.Count > UserEnrollmentData.MaxFingersPerUser)
                throw new InvalidOperationException($"Cannot enroll more than {UserEnrollmentData.MaxFingersPerUser} fingers per user SID.");

            string path = GetTemplateFilePath(data.UserSid);

            lock (_fileLock)
            {
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(fs, Encoding.UTF8))
                {
                    writer.Write(UserEnrollmentData.MagicHeader);
                    writer.Write(UserEnrollmentData.CurrentFormatVersion);
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
                }

                ApplySecurityAcl(path, data.UserSid);
            }
        }

        public static bool DeleteFinger(string userSid, int fingerIndex)
        {
            var data = LoadEnrollment(userSid);
            if (data == null) return false;

            int removed = data.Fingers.RemoveAll(f => f.FingerIndex == fingerIndex);
            if (removed == 0) return false;

            if (data.Fingers.Count == 0)
            {
                DeleteEnrollment(userSid);
            }
            else
            {
                SaveEnrollment(data);
            }

            return true;
        }

        public static bool DeleteEnrollment(string userSid)
        {
            string path = GetTemplateFilePath(userSid);
            lock (_fileLock)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        File.Delete(path);
                        return true;
                    }
                    catch { return false; }
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
                    list.Add(sid);
                }
            }

            return list;
        }

        public static List<UserEnrollmentData> LoadAllEnrollments()
        {
            var sids = GetAllEnrolledSids();
            var list = new List<UserEnrollmentData>();
            foreach (var sid in sids)
            {
                var data = LoadEnrollment(sid);
                if (data != null) list.Add(data);
            }
            return list;
        }

        private static void ApplySecurityAcl(string filePath, string userSid)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var sec = fileInfo.GetAccessControl();

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

                // Grant the specific user SID Read/Write access to their own template file
                try
                {
                    var targetUser = new SecurityIdentifier(userSid);
                    sec.AddAccessRule(new FileSystemAccessRule(
                        targetUser,
                        FileSystemRights.ReadAndExecute | FileSystemRights.Modify,
                        AccessControlType.Allow));
                }
                catch { }

                fileInfo.SetAccessControl(sec);
            }
            catch
            {
                // Non-fatal if running in non-elevated user context during testing
            }
        }
    }
}
