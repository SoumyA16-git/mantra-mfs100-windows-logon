using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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
                        string response = await ProcessRequestAsync(line);
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

        private async Task<string> ProcessRequestAsync(string requestJson)
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

                default:
                    // Default deadline of 5000ms for other commands (init, match, uninit)
                    return await _workerHost.SendCommandAsync(requestJson, 5000);
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
