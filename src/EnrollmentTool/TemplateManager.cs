using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using MFS100BridgeSvc;
using MFS100Worker;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

    /// <summary>
    /// Manages user biometric enrollments.
    /// Under the Privileged Broker Architecture (AC 2.8), all operations default to the
    /// Session-0 Bridge Service via named pipe (\\.\pipe\MFS100Bridge), ensuring standard users
    /// have ZERO direct file access to encrypted templates.
    /// In local elevated test mode, it interacts with EncryptedTemplateStore directly.
    /// </summary>
    public static class TemplateManager
    {
        public const string PipeName = "MFS100Bridge";

        public static bool IsServiceAvailable()
        {
            try
            {
                string rsp = SendPipeCommand(JsonConvert.SerializeObject(new { cmd = "ping" }), 1000);
                var j = JObject.Parse(rsp);
                return j["ok"]?.Value<bool>() == true;
            }
            catch
            {
                return false;
            }
        }

        public static string SendPipeCommand(string requestJson, int timeoutMs = 5000)
        {
            using var client = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.None,
                TokenImpersonationLevel.Impersonation);
            client.Connect(timeoutMs);

            using var reader = new StreamReader(client, new UTF8Encoding(false));
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };

            writer.WriteLine(requestJson);
            string? line = reader.ReadLine();
            return line ?? throw new IOException("Received empty response from bridge service");
        }

        public static bool IsUserEnrolled(string userSid)
        {
            if (IsServiceAvailable())
            {
                try
                {
                    string req = JsonConvert.SerializeObject(new { cmd = "get_enrollment", sid = userSid });
                    string rsp = SendPipeCommand(req, 3000);
                    var j = JObject.Parse(rsp);
                    return j["enrolled"]?.Value<bool>() == true;
                }
                catch { }
            }

            // Fallback for local administrative testing
            return EncryptedTemplateStore.IsUserEnrolled(userSid);
        }

        public static UserEnrollmentData? LoadEnrollment(string userSid)
        {
            if (IsServiceAvailable())
            {
                try
                {
                    string req = JsonConvert.SerializeObject(new { cmd = "get_enrollment", sid = userSid });
                    string rsp = SendPipeCommand(req, 3000);
                    var j = JObject.Parse(rsp);

                    if (j["ok"]?.Value<bool>() != true || j["enrolled"]?.Value<bool>() != true)
                        return null;

                    var summary = j["summary"];
                    if (summary == null) return null;

                    var data = new UserEnrollmentData
                    {
                        UserSid = summary["UserSid"]?.Value<string>() ?? userSid,
                        EnrolledUtc = summary["EnrolledUtc"]?.Value<DateTime>() ?? DateTime.UtcNow
                    };

                    var fingersArr = summary["Fingers"] as JArray;
                    if (fingersArr != null)
                    {
                        foreach (var f in fingersArr)
                        {
                            data.Fingers.Add(new EnrolledFinger
                            {
                                FingerIndex = f["FingerIndex"]?.Value<int>() ?? 0,
                                FingerLabel = f["FingerLabel"]?.Value<string>() ?? string.Empty,
                                Quality = f["Quality"]?.Value<int>() ?? 0,
                                EnrolledUtc = f["EnrolledUtc"]?.Value<DateTime>() ?? DateTime.UtcNow
                            });
                        }
                    }

                    return data;
                }
                catch { }
            }

            // Fallback for local administrative testing (direct DPAPI load)
            var record = EncryptedTemplateStore.LoadEnrollment(userSid);
            if (record == null) return null;

            return new UserEnrollmentData
            {
                UserSid = record.UserSid,
                EnrolledUtc = record.EnrolledUtc,
                Fingers = record.Fingers.Select(f => new EnrolledFinger
                {
                    FingerIndex = f.FingerIndex,
                    FingerLabel = f.FingerLabel,
                    Quality = f.Quality,
                    EnrolledUtc = f.EnrolledUtc,
                    Template = f.Template
                }).ToList()
            };
        }

        public static void SaveEnrollment(UserEnrollmentData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));

            if (IsServiceAvailable())
            {
                foreach (var f in data.Fingers)
                {
                    var req = new
                    {
                        cmd = "enroll_finger",
                        sid = data.UserSid,
                        fingerIndex = f.FingerIndex,
                        label = f.FingerLabel,
                        quality = f.Quality,
                        template = Convert.ToBase64String(f.Template)
                    };
                    string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), 5000);
                    var j = JObject.Parse(rspJson);
                    if (j["ok"]?.Value<bool>() != true)
                    {
                        string err = j["errorMsg"]?.Value<string>() ?? "Unknown service error";
                        throw new InvalidOperationException($"Service failed to enroll finger: {err}");
                    }
                }
                return;
            }

            // Fallback for local administrative testing
            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            string callerSid = isAdmin ? (WindowsIdentity.GetCurrent().User?.Value ?? data.UserSid) : data.UserSid;

            var record = new UserEnrollmentRecord
            {
                UserSid = data.UserSid,
                EnrolledUtc = data.EnrolledUtc,
                Fingers = data.Fingers.Select(f => new EnrolledFingerRecord
                {
                    FingerIndex = f.FingerIndex,
                    FingerLabel = f.FingerLabel,
                    Quality = f.Quality,
                    EnrolledUtc = f.EnrolledUtc,
                    Template = f.Template
                }).ToList()
            };

            EncryptedTemplateStore.SaveEnrollment(callerSid, isAdmin, record);
        }

        public static bool DeleteFinger(string userSid, int fingerIndex)
        {
            if (IsServiceAvailable())
            {
                try
                {
                    var req = new { cmd = "delete_finger", sid = userSid, fingerIndex = fingerIndex };
                    string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), 3000);
                    var j = JObject.Parse(rspJson);
                    return j["ok"]?.Value<bool>() == true;
                }
                catch { return false; }
            }

            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            string callerSid = isAdmin ? (WindowsIdentity.GetCurrent().User?.Value ?? userSid) : userSid;

            return EncryptedTemplateStore.DeleteFinger(callerSid, isAdmin, userSid, fingerIndex);
        }

        public static bool DeleteEnrollment(string userSid)
        {
            if (IsServiceAvailable())
            {
                try
                {
                    var req = new { cmd = "delete_enrollment", sid = userSid };
                    string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), 3000);
                    var j = JObject.Parse(rspJson);
                    return j["ok"]?.Value<bool>() == true;
                }
                catch { return false; }
            }

            var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
            bool isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
            string callerSid = isAdmin ? (WindowsIdentity.GetCurrent().User?.Value ?? userSid) : userSid;

            return EncryptedTemplateStore.DeleteEnrollment(callerSid, isAdmin, userSid);
        }

        public static List<string> GetAllEnrolledSids()
        {
            if (IsServiceAvailable())
            {
                try
                {
                    var req = new { cmd = "list_enrolled_sids" };
                    string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), 3000);
                    var j = JObject.Parse(rspJson);
                    var sidsArr = j["sids"] as JArray;
                    if (sidsArr != null)
                        return sidsArr.Select(s => s.Value<string>()!).ToList();
                }
                catch { }
            }

            return EncryptedTemplateStore.GetAllEnrolledSids();
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

        public static (bool ok, byte[]? isoTemplate, int quality, int nfiq, string? errorMsg) CaptureViaService(int timeoutMs = 15000)
        {
            try
            {
                var req = new { cmd = "capture", timeoutMs = timeoutMs };
                string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), timeoutMs + 3000);
                var j = JObject.Parse(rspJson);

                bool ok = j["ok"]?.Value<bool>() == true;
                if (!ok)
                {
                    string err = j["errorMsg"]?.Value<string>() ?? "Capture failed in service";
                    return (false, null, 0, 0, err);
                }

                string? isoB64 = j["isoTemplate"]?.Value<string>() ?? j["template"]?.Value<string>();
                if (string.IsNullOrEmpty(isoB64))
                {
                    return (false, null, 0, 0, "No template returned in service response");
                }

                byte[] isoBytes = Convert.FromBase64String(isoB64!);
                int q = j["quality"]?.Value<int>() ?? 0;
                int nfiq = j["nfiq"]?.Value<int>() ?? 0;

                return (true, isoBytes, q, nfiq, null);
            }
            catch (Exception ex)
            {
                return (false, null, 0, 0, $"Service communication error: {ex.Message}");
            }
        }

        public static (bool ok, bool accessGranted, int score, string? label, string? errorMsg) MatchUserViaService(string userSid, byte[] probeTemplate, int threshold = 1400)
        {
            try
            {
                var req = new
                {
                    cmd = "match_user",
                    sid = userSid,
                    probe = Convert.ToBase64String(probeTemplate),
                    threshold = threshold
                };

                string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), 8000);
                var j = JObject.Parse(rspJson);

                bool ok = j["ok"]?.Value<bool>() == true;
                bool accessGranted = j["accessGranted"]?.Value<bool>() == true;
                int score = j["score"]?.Value<int>() ?? 0;
                string? label = j["label"]?.Value<string>();
                string? errorMsg = j["errorMsg"]?.Value<string>();

                return (ok, accessGranted, score, label, errorMsg);
            }
            catch (Exception ex)
            {
                return (false, false, 0, null, $"Service error: {ex.Message}");
            }
        }

        public static (bool ok, bool matched, string? userSid, int score, string? errorMsg) IdentifyUserViaService(byte[] probeTemplate, int threshold = 1400)
        {
            try
            {
                var req = new
                {
                    cmd = "identify_user",
                    probe = Convert.ToBase64String(probeTemplate),
                    threshold = threshold
                };

                string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), 10000);
                var j = JObject.Parse(rspJson);

                bool ok = j["ok"]?.Value<bool>() == true;
                bool matched = j["matched"]?.Value<bool>() == true;
                string? sid = j["userSid"]?.Value<string>();
                int score = j["score"]?.Value<int>() ?? 0;
                string? errorMsg = j["errorMsg"]?.Value<string>();

                return (ok, matched, sid, score, errorMsg);
            }
            catch (Exception ex)
            {
                return (false, false, null, 0, $"Service error: {ex.Message}");
            }
        }

        public static (bool ok, string msg) SaveVaultPassword(string userSid, string password)
        {
            try
            {
                if (IsServiceAvailable())
                {
                    var req = new
                    {
                        cmd = "save_vault_credential",
                        sid = userSid,
                        password = password
                    };
                    string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), 5000);
                    var j = JObject.Parse(rspJson);
                    bool ok = j["ok"]?.Value<bool>() == true;
                    string msg = j["msg"]?.Value<string>() ?? (ok ? "Password saved to vault." : "Failed to save password.");
                    return (ok, msg);
                }

                // Fallback for direct local testing
                CredentialVault.SaveCredential(userSid, password);
                return (true, "Password saved to vault directly via DPAPI.");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to save vault password: {ex.Message}");
            }
        }

        public static bool HasVaultPassword(string userSid)
        {
            try
            {
                if (IsServiceAvailable())
                {
                    var req = new { cmd = "has_vault_credential", sid = userSid };
                    string rspJson = SendPipeCommand(JsonConvert.SerializeObject(req), 3000);
                    var j = JObject.Parse(rspJson);
                    return j["hasPassword"]?.Value<bool>() == true;
                }
                return CredentialVault.HasCredential(userSid);
            }
            catch
            {
                return false;
            }
        }

        public static string GetTemplateFilePath(string userSid) =>
            EncryptedTemplateStore.GetTemplateFilePath(userSid);
    }
}
