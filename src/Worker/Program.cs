using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MFS100Worker
{
    /// <summary>
    /// MFS100Worker.exe — x86, .NET 4.8
    ///
    /// Hosts the Mantra MFS100 SDK in a dedicated 32-bit subprocess.
    /// Communicates via stdin/stdout: one JSON line per message.
    /// The parent process (bridge service) kills this process on deadline expiry.
    ///
    /// Modes:
    ///   (no args)          Pipe mode: read commands from stdin, write responses to stdout
    ///   --test             Interactive test mode: runs FAR/FRR capture sequence
    ///   --test-service     Non-interactive service simulation (for Gate 1B)
    /// </summary>
    internal static class Program
    {
        [System.STAThread]
        static void Main(string[] args)
        {
            // Redirect SDK stdout chatter (quality printouts) to our log file
            // so it does not corrupt the JSON protocol on stdout.
            WorkerLog.Init();
            RedirectSdkStdout();

            bool testMode        = HasArg(args, "--test");
            bool testServiceMode = HasArg(args, "--test-service");
            bool testMatcherMode = HasArg(args, "--test-matcher");

            if (testMatcherMode)
            {
                StandaloneMatcherTest.Run();
                return;
            }

            if (testMode)
            {
                InteractiveTestRunner.Run();
                return;
            }

            if (testServiceMode)
            {
                ServiceSimulationTest.Run();
                return;
            }

            // Default: pipe mode — used by the bridge service
            PipeMode.Run();
        }

        static bool HasArg(string[] args, string flag) =>
            Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// The MFS100 SDK prints "MFS100AutoCapture Quality = N" to stdout on
        /// every poll iteration. We redirect Console.Out before the SDK is loaded
        /// so the SDK's native P/Invoke calls still go to the real stdout handle,
        /// but our managed Console.WriteLine goes to a captured stream.
        ///
        /// For the JSON protocol we use the raw stdout FileStream directly.
        /// </summary>
        static void RedirectSdkStdout()
        {
            // Save raw stdout handle for JSON protocol writes
            var rawStdout = new FileStream(
                new Microsoft.Win32.SafeHandles.SafeFileHandle(
                    GetStdHandle(STD_OUTPUT_HANDLE), false),
                FileAccess.Write, bufferSize: 1, isAsync: false);

            WorkerLog.RawStdout = new StreamWriter(rawStdout, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            // Now redirect managed Console.Out to our log (captures SDK printf-style output)
            Console.SetOut(new WorkerLog.SdkOutputCapture());
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int nStdHandle);
        const int STD_OUTPUT_HANDLE = -11;
    }
}
