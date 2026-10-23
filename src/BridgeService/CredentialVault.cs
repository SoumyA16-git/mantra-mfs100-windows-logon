using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace MFS100BridgeSvc
{
    /// <summary>
    /// DPAPI-protected Credential Vault running in Session 0 (SYSTEM).
    /// Stores machine-encrypted local logon credentials tied to user SIDs,
    /// enabling seamless cold-boot one-touch authentication upon biometric match.
    /// </summary>
    public static class CredentialVault
    {
        private static readonly object _lock = new object();

        public static string GetVaultDirectory()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MFS100CP", "vault");

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            ApplyVaultSecurity(dir);
            return dir;
        }

        private static void ApplyVaultSecurity(string dirPath)
        {
            try
            {
                var dInfo = new DirectoryInfo(dirPath);
                var ds = new DirectorySecurity();
                ds.SetAccessRuleProtection(true, false); // Block inheritance

                // SYSTEM: FullControl
                ds.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                // Administrators: FullControl
                ds.AddAccessRule(new FileSystemAccessRule(
                    new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));

                dInfo.SetAccessControl(ds);
            }
            catch (Exception ex)
            {
                ServiceLog.Error($"Failed to set vault directory security: {ex.Message}");
            }
        }

        public static void SaveCredential(string userSid, string password)
        {
            if (string.IsNullOrEmpty(userSid) || password == null) return;

            lock (_lock)
            {
                string dir = GetVaultDirectory();
                string filePath = Path.Combine(dir, $"{userSid}.dat");

                byte[] plainBytes = Encoding.UTF8.GetBytes(password);
                byte[] entropy = Encoding.UTF8.GetBytes($"MFS100_VAULT_{userSid}");
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.LocalMachine);

                File.WriteAllBytes(filePath, cipherBytes);
                ServiceLog.Info($"Saved DPAPI-protected credential vault for SID '{userSid}'.");
            }
        }

        public static string? LoadCredential(string userSid)
        {
            if (string.IsNullOrEmpty(userSid)) return null;

            lock (_lock)
            {
                string dir = GetVaultDirectory();
                string filePath = Path.Combine(dir, $"{userSid}.dat");

                if (!File.Exists(filePath))
                {
                    return null;
                }

                try
                {
                    byte[] cipherBytes = File.ReadAllBytes(filePath);
                    byte[] entropy = Encoding.UTF8.GetBytes($"MFS100_VAULT_{userSid}");
                    byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, entropy, DataProtectionScope.LocalMachine);
                    return Encoding.UTF8.GetString(plainBytes);
                }
                catch (Exception ex)
                {
                    ServiceLog.Error($"Failed to unseal credential vault for SID '{userSid}': {ex.Message}");
                    return null;
                }
            }
        }

        public static bool HasCredential(string userSid)
        {
            if (string.IsNullOrEmpty(userSid)) return false;
            string dir = GetVaultDirectory();
            string filePath = Path.Combine(dir, $"{userSid}.dat");
            return File.Exists(filePath);
        }

        public static void DeleteCredential(string userSid)
        {
            if (string.IsNullOrEmpty(userSid)) return;
            lock (_lock)
            {
                string dir = GetVaultDirectory();
                string filePath = Path.Combine(dir, $"{userSid}.dat");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    ServiceLog.Info($"Deleted credential vault for SID '{userSid}'.");
                }
            }
        }
    }
}
