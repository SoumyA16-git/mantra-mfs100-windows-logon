using System;
using System.ServiceProcess;
using System.Threading;

namespace MFS100BridgeSvc
{
    internal static class Program
    {
        static void Main(string[] args)
        {
            ServiceLog.Init();

            bool isConsoleMode = Environment.UserInteractive || HasArg(args, "--console");

            if (isConsoleMode)
            {
                Console.WriteLine("==================================================");
                Console.WriteLine("   MFS100 Bridge Service — Interactive Mode       ");
                Console.WriteLine($"   PID: {System.Diagnostics.Process.GetCurrentProcess().Id}");
                Console.WriteLine("==================================================");

                var service = new BridgeService();
                service.StartInteractive(args);

                Console.WriteLine("\nBridge service is running. Press Ctrl+C or Enter to exit...\n");

                var exitEvent = new ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    exitEvent.Set();
                };

                // Wait for Enter key or Ctrl+C
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        Console.ReadLine();
                        exitEvent.Set();
                    }
                    catch { }
                });

                exitEvent.WaitOne();

                Console.WriteLine("Stopping bridge service...");
                service.StopInteractive();
                Console.WriteLine("Service stopped.");
            }
            else
            {
                ServiceBase.Run(new BridgeService());
            }
        }

        static bool HasArg(string[] args, string flag) =>
            Array.Exists(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    }
}
