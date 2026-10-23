using System;
using System.IO;
using System.Text;

namespace MFS100BridgeSvc
{
    public static class ServiceLog
    {
        private static readonly object _fileLock = new object();
        private static string? _logPath;

        public static void Init()
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MFS100CP", "logs");
                Directory.CreateDirectory(logDir);
                _logPath = Path.Combine(logDir, $"bridge_svc_{DateTime.Now:yyyyMMdd}.log");
            }
            catch { }
        }

        public static void Info(string msg)  => Log("INFO", msg);
        public static void Warn(string msg)  => Log("WARN", msg);
        public static void Error(string msg) => Log("ERROR", msg);

        private static void Log(string level, string msg)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";

            try
            {
                Console.WriteLine(line);
            }
            catch { }

            if (_logPath != null)
            {
                lock (_fileLock)
                {
                    try
                    {
                        File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
                    }
                    catch { }
                }
            }
        }
    }
}
