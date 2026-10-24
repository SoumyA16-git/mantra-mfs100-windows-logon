using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MFS100BridgeSvc
{
    /// <summary>
    /// WorkerHost manages the lifecycle of the 32-bit MFS100Worker.exe subprocess.
    /// It enforces hard execution deadlines, killing and restarting the worker if an SDK call blocks.
    /// All communication happens over redirected standard input/output in single-line JSON format.
    /// </summary>
    public class WorkerHost : IDisposable
    {
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _cmdSemaphore = new SemaphoreSlim(1, 1);
        private Process? _workerProcess;
        private StreamWriter? _stdinWriter;
        private StreamReader? _stdoutReader;
        private string? _workerExePath;
        private bool _disposed;

        public int WorkerPid
        {
            get
            {
                lock (_lock)
                {
                    try
                    {
                        if (_workerProcess != null && !_workerProcess.HasExited)
                            return _workerProcess.Id;
                    }
                    catch { }
                    return 0;
                }
            }
        }

        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    try
                    {
                        return _workerProcess != null && !_workerProcess.HasExited;
                    }
                    catch { return false; }
                }
            }
        }

        public WorkerHost()
        {
            _workerExePath = LocateWorkerExe();
            if (_workerExePath == null)
            {
                ServiceLog.Error("MFS100Worker.exe could not be found!");
            }
            else
            {
                ServiceLog.Info($"Worker executable located at: {_workerExePath}");
            }
        }

        private string? LocateWorkerExe()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string p1 = Path.Combine(baseDir, "MFS100Worker.exe");
            if (File.Exists(p1)) return p1;

            string p2 = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\Worker\bin\x86\Release\net48\MFS100Worker.exe"));
            if (File.Exists(p2)) return p2;

            string p3 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MFS100CP", "MFS100Worker.exe");
            if (File.Exists(p3)) return p3;

            string p4 = @"C:\Program Files\MFS100CP\MFS100Worker.exe";
            if (File.Exists(p4)) return p4;

            return null;
        }

        private void EnsureWorkerRunning()
        {
            lock (_lock)
            {
                if (_workerProcess != null && !_workerProcess.HasExited)
                    return;

                if (string.IsNullOrEmpty(_workerExePath) || !File.Exists(_workerExePath))
                {
                    _workerExePath = LocateWorkerExe();
                    if (_workerExePath == null || !File.Exists(_workerExePath))
                        throw new FileNotFoundException("MFS100Worker.exe not found.");
                }

                ServiceLog.Info("Spawning MFS100Worker.exe subprocess...");

                var psi = new ProcessStartInfo
                {
                    FileName = _workerExePath,
                    Arguments = "", // Pipe mode by default
                    WorkingDirectory = Path.GetDirectoryName(_workerExePath),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                    StandardErrorEncoding = new UTF8Encoding(false)
                };

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                proc.Exited += (s, e) =>
                {
                    ServiceLog.Warn($"MFS100Worker process has exited (exit code: {(proc.HasExited ? proc.ExitCode : -1)})");
                };

                if (!proc.Start())
                    throw new InvalidOperationException("Failed to start MFS100Worker.exe");

                _workerProcess = proc;
                _stdinWriter = new StreamWriter(proc.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
                _stdoutReader = new StreamReader(proc.StandardOutput.BaseStream, new UTF8Encoding(false));

                // Drain stderr asynchronously to avoid blocking
                Task.Run(() =>
                {
                    try
                    {
                        while (!proc.HasExited)
                        {
                            string? err = proc.StandardError.ReadLine();
                            if (err != null && err.Length > 0)
                                ServiceLog.Warn($"[Worker-Err] {err}");
                        }
                    }
                    catch { }
                });

                ServiceLog.Info($"MFS100Worker.exe started successfully (PID: {proc.Id})");
            }
        }

        public async Task<string> SendCommandAsync(string jsonCommand, int deadlineMs)
        {
            if (_disposed)
                return Protocol.CreateErrorJson(-1, "Service is shutting down");

            await _cmdSemaphore.WaitAsync();
            try
            {
                Task<string> executionTask;

                lock (_lock)
                {
                    EnsureWorkerRunning();

                    var proc = _workerProcess!;
                    var writer = _stdinWriter!;
                    var reader = _stdoutReader!;

                    executionTask = Task.Run(async () =>
                    {
                        try
                        {
                            await writer.WriteLineAsync(jsonCommand);
                            string? line;
                            while (true)
                            {
                                line = await reader.ReadLineAsync();
                                if (line == null)
                                    throw new IOException("Worker returned EOF");
                                line = line.Trim();
                                
                                int jsonStart = line.IndexOf('{');
                                int jsonEnd = line.LastIndexOf('}');
                                if (jsonStart >= 0 && jsonEnd > jsonStart)
                                {
                                    string jsonCandidate = line.Substring(jsonStart, jsonEnd - jsonStart + 1).Trim();
                                    try
                                    {
                                        var parsed = JObject.Parse(jsonCandidate);
                                        if (parsed["ok"] != null || parsed["cmd"] != null || parsed["score"] != null)
                                        {
                                            return jsonCandidate;
                                        }
                                    }
                                    catch { }
                                }

                                // Native SDK printf chatter (e.g. "MFS100AutoCapture Quality = N") - ignore it
                                ServiceLog.Info($"[Worker-IgnoredChatter] {line}");
                            }
                        }
                        catch (Exception ex)
                        {
                            ServiceLog.Error($"Communication error with worker: {ex.Message}");
                            throw;
                        }
                    });
                }

                // Enforce active execution deadline (ignoring periods when system is suspended/asleep)
                int elapsedActiveMs = 0;
                while (elapsedActiveMs < deadlineMs)
                {
                    if (executionTask.IsCompleted) break;
                    await Task.Delay(250);
                    if (!BridgeService.IsSystemSuspending)
                    {
                        elapsedActiveMs += 250;
                    }
                }

                if (!executionTask.IsCompleted)
                {
                    ServiceLog.Error($"Worker timed out! Exceeded active deadline of {deadlineMs}ms. Killing and restarting worker...");
                    KillAndRestart();
                    return Protocol.CreateErrorJson(-1140, $"Worker timed out after {deadlineMs}ms");
                }

                try
                {
                    return await executionTask;
                }
                catch (Exception ex)
                {
                    KillAndRestart();
                    return Protocol.CreateErrorJson(-1, $"Worker communication failure: {ex.Message}");
                }
            }
            finally
            {
                _cmdSemaphore.Release();
            }
        }

        public void KillAndRestart()
        {
            lock (_lock)
            {
                try
                {
                    if (_workerProcess != null && !_workerProcess.HasExited)
                    {
                        ServiceLog.Warn($"Terminating worker process PID {_workerProcess.Id}...");
                        _workerProcess.Kill();
                        _workerProcess.WaitForExit(2000);
                    }
                }
                catch (Exception ex)
                {
                    ServiceLog.Error($"Error killing worker: {ex.Message}");
                }
                finally
                {
                    _workerProcess?.Dispose();
                    _workerProcess = null;
                    _stdinWriter = null;
                    _stdoutReader = null;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                try
                {
                    if (_workerProcess != null && !_workerProcess.HasExited)
                    {
                        if (_stdinWriter != null)
                        {
                            try { _stdinWriter.WriteLine("{\"cmd\":\"uninit\"}"); }
                            catch { }
                        }

                        if (!_workerProcess.WaitForExit(1500))
                        {
                            _workerProcess.Kill();
                        }
                    }
                }
                catch { }
                finally
                {
                    _workerProcess?.Dispose();
                    _workerProcess = null;
                }
            }
        }
    }
}
