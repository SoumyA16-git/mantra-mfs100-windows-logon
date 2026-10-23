using System;
using System.IO;
using System.Text;

namespace MFS100Worker
{
    /// <summary>
    /// Logging for the worker process.
    /// Log goes to %ProgramData%\MFS100CP\logs\worker_YYYYMMDD.log
    /// If that path is unavailable (e.g. first run before installer), falls back to %TEMP%.
    /// </summary>
    internal static class WorkerLog
    {
        static readonly object _lock = new object();
        static StreamWriter? _log;

        /// <summary>Raw stdout stream — used for JSON protocol output only.</summary>
        internal static StreamWriter? RawStdout;

        internal static void Init()
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MFS100CP", "logs");
                Directory.CreateDirectory(logDir);
                string logPath = Path.Combine(logDir,
                    $"worker_{DateTime.Now:yyyyMMdd}.log");
                _log = new StreamWriter(logPath, append: true, new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
                Info($"=== Worker started (PID {System.Diagnostics.Process.GetCurrentProcess().Id}) ===");
            }
            catch
            {
                // If log can't be created, continue silently — do not crash the worker.
            }
        }

        internal static void Info(string msg)  => Write("INFO ", msg);
        internal static void Warn(string msg)  => Write("WARN ", msg);
        internal static void Error(string msg) => Write("ERROR", msg);

        static void Write(string level, string msg)
        {
            lock (_lock)
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] {msg}";
                try { _log?.WriteLine(line); } catch { }
            }
        }

        /// <summary>
        /// Captures SDK stdout chatter ("MFS100AutoCapture Quality = N") and writes it
        /// to the worker log instead of the JSON protocol stdout.
        /// </summary>
        internal class SdkOutputCapture : TextWriter
        {
            readonly StringBuilder _buf = new StringBuilder();
            public override Encoding Encoding => Encoding.UTF8;

            public override void Write(char value)
            {
                if (value == '\n')
                {
                    string line = _buf.ToString().TrimEnd('\r');
                    _buf.Clear();
                    if (line.Length > 0)
                        WorkerLog.Write("SDK  ", line);
                }
                else
                {
                    _buf.Append(value);
                }
            }

            public override void Write(string? value)
            {
                if (value != null)
                    foreach (char c in value) Write(c);
            }

            public override void WriteLine(string? value)
            {
                Write(value ?? "");
                Write('\n');
            }

            protected override void Dispose(bool disposing) { }
        }
    }
}
