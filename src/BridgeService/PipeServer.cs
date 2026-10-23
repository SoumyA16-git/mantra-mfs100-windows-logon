using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFS100BridgeSvc
{
    public class PipeServer : IDisposable
    {
        private readonly WorkerHost _workerHost;
        private CancellationTokenSource? _cts;
        private Task? _listenerTask;
        private bool _disposed;

        public PipeServer(WorkerHost workerHost)
        {
            _workerHost = workerHost;
        }

        public void Start()
        {
            if (_listenerTask != null) return;

            _cts = new CancellationTokenSource();
            _listenerTask = Task.Run(() => ListenLoopAsync(_cts.Token));
            ServiceLog.Info($"Named pipe server started on \\\\.\\pipe\\{Protocol.PipeName}");
        }

        public void Stop()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
            _listenerTask = null;
            ServiceLog.Info("Named pipe server stopped.");
        }

        private PipeSecurity CreatePipeSecurity()
        {
            var ps = new PipeSecurity();

            // Grant current user/process identity FullControl
            var currentSid = WindowsIdentity.GetCurrent().User;
            if (currentSid != null)
            {
                ps.AddAccessRule(new PipeAccessRule(
                    currentSid,
                    PipeAccessRights.FullControl,
                    AccessControlType.Allow));
            }

            // Grant LocalSystem FullControl
            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            // Grant Administrators FullControl
            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            // Grant Authenticated Users ReadWrite access for logon and enrollment clients
            ps.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            return ps;
        }

        private async Task ListenLoopAsync(CancellationToken ct)
        {
            var pipeSecurity = CreatePipeSecurity();
            bool isFirstInstance = true;

            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream? serverStream = null;
                try
                {
                    if (isFirstInstance)
                    {
                        serverStream = new NamedPipeServerStream(
                            Protocol.PipeName,
                            PipeDirection.InOut,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            8192,
                            8192,
                            pipeSecurity);
                        isFirstInstance = false;
                    }
                    else
                    {
                        serverStream = new NamedPipeServerStream(
                            Protocol.PipeName,
                            PipeDirection.InOut,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous,
                            8192,
                            8192);
                    }

                    await Task.Factory.FromAsync(serverStream.BeginWaitForConnection, serverStream.EndWaitForConnection, null);

                    ServiceLog.Info("Client connected to named pipe.");
                    _ = Task.Run(() => HandleClientAsync(serverStream, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    serverStream?.Dispose();
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        serverStream?.Dispose();
                        break;
                    }
                    ServiceLog.Error($"Pipe listener error: {ex.Message}");
                    serverStream?.Dispose();
                    await Task.Delay(500, ct);
                }
            }
        }

        private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
        {
            string callerSid = string.Empty;
            bool callerIsAdmin = false;

            try
            {
                pipe.RunAsClient(() =>
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    callerSid = identity.User?.Value ?? string.Empty;
                    var principal = new WindowsPrincipal(identity);
                    callerIsAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);
                });
                ServiceLog.Info($"Pipe client identity resolved: SID='{callerSid}', IsAdmin={callerIsAdmin}");
            }
            catch (Exception ex)
            {
                ServiceLog.Warn($"Could not determine pipe caller identity: {ex.Message}");
            }

            using (pipe)
            using (var reader = new StreamReader(pipe, new UTF8Encoding(false)))
            using (var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true })
            {
                try
                {
                    while (pipe.IsConnected && !ct.IsCancellationRequested)
                    {
                        string? line = await reader.ReadLineAsync();
                        if (line == null) break;

                        line = line.Trim();
                        if (line.Length == 0) continue;

                        ServiceLog.Info($"PIPE REQ: {line}");
                        string response = await ProcessRequestAsync(line, callerSid, callerIsAdmin);
                        ServiceLog.Info($"PIPE RSP: {response}");

                        await writer.WriteLineAsync(response);
                    }
                }
                catch (Exception ex)
                {
                    ServiceLog.Warn($"Pipe client error / disconnect: {ex.Message}");
                }
            }
            ServiceLog.Info("Client disconnected from named pipe.");
        }

        private async Task<string> ProcessRequestAsync(string requestJson, string callerSid, bool callerIsAdmin)
        {
            JObject req;
            try
            {
                req = JObject.Parse(requestJson);
            }
            catch
            {
                return Protocol.CreateErrorJson(-1, "Invalid JSON request format");
            }

            string? cmd = req["cmd"]?.Value<string>();
            if (string.IsNullOrEmpty(cmd))
                return Protocol.CreateErrorJson(-1, "Missing 'cmd' property in request");

            try
            {
                switch (cmd!.ToLowerInvariant())
                {
                    case "ping":
                        return JsonConvert.SerializeObject(new Protocol.PingResponse
                        {
                            Ok = true,
                            ServicePid = Process.GetCurrentProcess().Id,
                            WorkerPid = _workerHost.WorkerPid
                        }, Formatting.None);

                    case "status":
                        return JsonConvert.SerializeObject(new
                        {
                            ok = true,
                            servicePid = Process.GetCurrentProcess().Id,
                            workerPid = _workerHost.WorkerPid,
                            workerRunning = _workerHost.IsRunning
                        }, Formatting.None);

                    case "capture":
                    {
                        int timeoutMs = req["timeoutMs"]?.Value<int>() ?? 10000;
                        if (timeoutMs <= 0) timeoutMs = 10000;

                        // Hard deadline = requested timeout + 2500ms grace period
                        int deadline = timeoutMs + 2500;
                        return await _workerHost.SendCommandAsync(requestJson, deadline);
                    }

                    // -----------------------------------------------------------------
                    // Privileged Template Broker Commands (AC 2.8 - AC 2.12)
                    // -----------------------------------------------------------------

                    case "enroll_finger":
                    {
                        string? sid = req["sid"]?.Value<string>();
                        int fingerIndex = req["fingerIndex"]?.Value<int>() ?? 0;
                        string label = req["label"]?.Value<string>() ?? (fingerIndex == 0 ? "Primary Finger" : "Secondary Finger");
                        int quality = req["quality"]?.Value<int>() ?? 0;
                        string? templateBase64 = req["template"]?.Value<string>();

                        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(templateBase64))
                            return Protocol.CreateErrorJson(-1, "Missing 'sid' or 'template' parameter");

                        EncryptedTemplateStore.AuthorizeCaller(callerSid, callerIsAdmin, sid!);

                        byte[] templateBytes = Convert.FromBase64String(templateBase64!);
                        var record = EncryptedTemplateStore.LoadEnrollment(sid!) ?? new UserEnrollmentRecord
                        {
                            UserSid = sid!,
                            EnrolledUtc = DateTime.UtcNow
                        };

                        if (record.Fingers.Count >= UserEnrollmentRecord.MaxFingersPerUser &&
                            !record.Fingers.Any(f => f.FingerIndex == fingerIndex))
                        {
                            return Protocol.CreateErrorJson(-2, $"User already has maximum {UserEnrollmentRecord.MaxFingersPerUser} fingers enrolled.");
                        }

                        record.Fingers.RemoveAll(f => f.FingerIndex == fingerIndex);
                        record.Fingers.Add(new EnrolledFingerRecord
                        {
                            FingerIndex = fingerIndex,
                            FingerLabel = label,
                            Quality = quality,
                            EnrolledUtc = DateTime.UtcNow,
                            Template = templateBytes
                        });
                        record.Fingers = record.Fingers.OrderBy(f => f.FingerIndex).ToList();
                        record.EnrolledUtc = DateTime.UtcNow;

                        EncryptedTemplateStore.SaveEnrollment(callerSid, callerIsAdmin, record);

                        return JsonConvert.SerializeObject(new
                        {
                            ok = true,
                            msg = $"Finger {fingerIndex} enrolled successfully for {sid}."
                        }, Formatting.None);
                    }

                    case "get_enrollment":
                    {
                        string? sid = req["sid"]?.Value<string>();
                        if (string.IsNullOrEmpty(sid))
                            return Protocol.CreateErrorJson(-1, "Missing 'sid' parameter");

                        EncryptedTemplateStore.AuthorizeCaller(callerSid, callerIsAdmin, sid!);
                        var summary = EncryptedTemplateStore.GetEnrollmentSummary(callerSid, callerIsAdmin, sid!);

                        return JsonConvert.SerializeObject(new
                        {
                            ok = true,
                            enrolled = summary != null && summary.Fingers.Count > 0,
                            summary = summary
                        }, Formatting.None);
                    }

                    case "list_enrolled_sids":
                    {
                        var sids = EncryptedTemplateStore.GetAllEnrolledSids();
                        return JsonConvert.SerializeObject(new
                        {
                            ok = true,
                            sids = sids
                        }, Formatting.None);
                    }

                    case "delete_finger":
                    {
                        string? sid = req["sid"]?.Value<string>();
                        int fingerIndex = req["fingerIndex"]?.Value<int>() ?? 0;
                        if (string.IsNullOrEmpty(sid))
                            return Protocol.CreateErrorJson(-1, "Missing 'sid' parameter");

                        EncryptedTemplateStore.AuthorizeCaller(callerSid, callerIsAdmin, sid!);
                        bool ok = EncryptedTemplateStore.DeleteFinger(callerSid, callerIsAdmin, sid!, fingerIndex);
                        return JsonConvert.SerializeObject(new { ok = ok }, Formatting.None);
                    }

                    case "delete_enrollment":
                    {
                        string? sid = req["sid"]?.Value<string>();
                        if (string.IsNullOrEmpty(sid))
                            return Protocol.CreateErrorJson(-1, "Missing 'sid' parameter");

                        EncryptedTemplateStore.AuthorizeCaller(callerSid, callerIsAdmin, sid!);
                        bool ok = EncryptedTemplateStore.DeleteEnrollment(callerSid, callerIsAdmin, sid!);
                        return JsonConvert.SerializeObject(new { ok = ok }, Formatting.None);
                    }

                    case "match_user":
                    {
                        string? sid = req["sid"]?.Value<string>();
                        string? probeBase64 = req["probe"]?.Value<string>();
                        int threshold = req["threshold"]?.Value<int>() ?? 1400;

                        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(probeBase64))
                            return Protocol.CreateErrorJson(-1, "Missing 'sid' or 'probe' parameter");

                        var record = EncryptedTemplateStore.LoadEnrollment(sid!);
                        if (record == null || record.Fingers.Count == 0)
                        {
                            return JsonConvert.SerializeObject(new
                            {
                                ok = true,
                                accessGranted = false,
                                score = 0,
                                errorMsg = "User has no enrolled fingerprints."
                            }, Formatting.None);
                        }

                        int highestScore = -1;
                        EnrolledFingerRecord? bestFinger = null;

                        foreach (var f in record.Fingers)
                        {
                            var matchReq = new Protocol.CommandRequest
                            {
                                Cmd = "match",
                                Probe = probeBase64,
                                Gallery = Convert.ToBase64String(f.Template)
                            };
                            string matchRspJson = await _workerHost.SendCommandAsync(JsonConvert.SerializeObject(matchReq), 5000);
                            var matchRsp = JObject.Parse(matchRspJson);
                            int score = matchRsp["score"]?.Value<int>() ?? 0;
                            if (score > highestScore)
                            {
                                highestScore = score;
                                bestFinger = f;
                            }
                        }

                        bool accessGranted = highestScore >= threshold;
                        return JsonConvert.SerializeObject(new
                        {
                            ok = true,
                            accessGranted = accessGranted,
                            score = highestScore,
                            matchedFinger = accessGranted ? (int?)bestFinger?.FingerIndex : null,
                            label = accessGranted ? bestFinger?.FingerLabel : null
                        }, Formatting.None);
                    }

                    case "identify_user":
                    {
                        string? probeBase64 = req["probe"]?.Value<string>();
                        int threshold = req["threshold"]?.Value<int>() ?? 1400;

                        if (string.IsNullOrEmpty(probeBase64))
                            return Protocol.CreateErrorJson(-1, "Missing 'probe' parameter");

                        var allSids = EncryptedTemplateStore.GetAllEnrolledSids();
                        int bestScore = -1;
                        string? matchedSid = null;
                        int? matchedFinger = null;

                        foreach (var sid in allSids)
                        {
                            var record = EncryptedTemplateStore.LoadEnrollment(sid);
                            if (record == null) continue;

                            foreach (var f in record.Fingers)
                            {
                                var matchReq = new Protocol.CommandRequest
                                {
                                    Cmd = "match",
                                    Probe = probeBase64,
                                    Gallery = Convert.ToBase64String(f.Template)
                                };
                                string matchRspJson = await _workerHost.SendCommandAsync(JsonConvert.SerializeObject(matchReq), 5000);
                                var matchRsp = JObject.Parse(matchRspJson);
                                int score = matchRsp["score"]?.Value<int>() ?? 0;
                                ServiceLog.Info($"Match probe against SID '{sid}' finger {f.FingerIndex}: score={score}, ok={matchRsp["ok"]}");
                                if (score > bestScore)
                                {
                                    bestScore = score;
                                    if (score >= threshold)
                                    {
                                        matchedSid = sid;
                                        matchedFinger = f.FingerIndex;
                                    }
                                }
                            }
                        }

                        bool matched = bestScore >= threshold && matchedSid != null;
                        return JsonConvert.SerializeObject(new
                        {
                            ok = true,
                            matched = matched,
                            userSid = matchedSid,
                            score = bestScore,
                            matchedFinger = matchedFinger
                        }, Formatting.None);
                    }

                    default:
                        // Default deadline of 5000ms for other commands (init, match, uninit)
                        return await _workerHost.SendCommandAsync(requestJson, 5000);
                }
            }
            catch (UnauthorizedAccessException uex)
            {
                ServiceLog.Warn($"Authorization rejected: {uex.Message}");
                return Protocol.CreateErrorJson(403, uex.Message);
            }
            catch (System.Security.Cryptography.CryptographicException cex)
            {
                ServiceLog.Error($"Decryption failed (SID context mismatch): {cex.Message}");
                return Protocol.CreateErrorJson(400, $"Decryption failed (SID context mismatch): {cex.Message}");
            }
            catch (Exception ex)
            {
                ServiceLog.Error($"Internal error processing command '{cmd}': {ex.Message}");
                return Protocol.CreateErrorJson(-99, $"Internal server error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
