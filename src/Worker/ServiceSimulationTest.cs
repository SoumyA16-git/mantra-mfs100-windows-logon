using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;

namespace MFS100Worker
{
    /// <summary>
    /// Gate 1B: Non-interactive service simulation test.
    /// Run via Task Scheduler as SYSTEM (LocalSystem) to verify that the MFS100 SDK
    /// can initialise and capture from a non-interactive process in Session 0.
    ///
    /// Usage: MFS100Worker.exe --test-service
    ///
    /// Output is written to %ProgramData%\MFS100CP\logs\service_sim_test_YYYYMMDD.log
    /// and to stdout (for Task Scheduler to capture).
    ///
    /// Pass criteria:
    ///   - Init() returns 0
    ///   - IsConnected() returns true
    ///   - AutoCapture with 5s timeout, IsDetectFinger=false returns -1140 (Timeout) within 6s
    ///     (no finger placed — this tests the timeout without requiring the user to be present)
    ///
    /// The test DOES NOT require a finger to be placed.
    /// It only verifies that the SDK initialises and calls AutoCapture without crashing.
    /// </summary>
    internal static class ServiceSimulationTest
    {
        public static void Run()
        {
            // Set up stdout writer first before redirect
            var rawOut = new StreamWriter(Console.OpenStandardOutput(),
                new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(new WorkerLog.SdkOutputCapture()); // suppress SDK chatter from Console

            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MFS100CP", "logs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir,
                $"service_sim_test_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            using var log = new StreamWriter(logPath, append: false, new UTF8Encoding(false))
            {
                AutoFlush = true
            };

            void L(string msg)
            {
                string line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
                log.WriteLine(line);
                rawOut.WriteLine(line);
            }

            L("=== Gate 1B: Service Simulation Test ===");
            L($"Process: {Process.GetCurrentProcess().Id}");
            L($"User: {WindowsIdentity.GetCurrent().Name}");
            L($"IsInteractive: {Environment.UserInteractive}");
            L($"Session0: {!Environment.UserInteractive || IsSession0()}");
            L("");

            bool passed = true;

            // Step 1: Init
            L("Step 1: SDK Init");
            using var sdk = new SdkManager();
            var sw = Stopwatch.StartNew();
            var (initOk, initMsg) = sdk.Init();
            sw.Stop();

            if (initOk)
            {
                var (make, model, serial, ww, hh) = sdk.GetDeviceInfo();
                L($"  PASS Init() in {sw.ElapsedMilliseconds}ms: {make} {model} S={serial} {ww}x{hh}");
            }
            else
            {
                L($"  FAIL Init() in {sw.ElapsedMilliseconds}ms: {initMsg}");
                passed = false;
            }

            // Step 2: AutoCapture (no finger, 5s timeout) — tests timeout honors
            if (initOk)
            {
                L("");
                L("Step 2: AutoCapture timeout test (no finger, 5000ms)");
                sw.Restart();
                CaptureResult cr = sdk.Capture(5000);
                sw.Stop();

                bool timeoutOk = !cr.Ok && cr.ErrorCode == -1140; // -1140 = Timeout
                bool timingOk  = sw.ElapsedMilliseconds >= 4500 && sw.ElapsedMilliseconds <= 7000;

                L($"  Result: ok={cr.Ok} code={cr.ErrorCode} msg={cr.ErrorMsg}");
                L($"  Elapsed: {sw.ElapsedMilliseconds}ms (expected 5000±1500ms)");
                L($"  TimeoutError: {(timeoutOk ? "PASS" : "FAIL")}");
                L($"  TimingWindow: {(timingOk ? "PASS" : $"FAIL (elapsed={sw.ElapsedMilliseconds}ms)")}");

                if (!timeoutOk || !timingOk) passed = false;
            }

            // Step 3: Uninit + Re-Init
            if (initOk)
            {
                L("");
                L("Step 3: Uninit then Re-Init");
                sdk.Uninit();
                sw.Restart();
                var (reinitOk, reinitMsg) = sdk.Init();
                sw.Stop();

                if (reinitOk)
                    L($"  PASS Re-Init in {sw.ElapsedMilliseconds}ms");
                else
                {
                    L($"  FAIL Re-Init: {reinitMsg}");
                    passed = false;
                }
            }

            L("");
            L($"=== Gate 1B Result: {(passed ? "PASS" : "FAIL")} ===");
            L($"Log written to: {logPath}");
        }

        static bool IsSession0()
        {
            try
            {
                // Session 0 processes have SessionId = 0
                return Process.GetCurrentProcess().SessionId == 0;
            }
            catch { return false; }
        }
    }
}
