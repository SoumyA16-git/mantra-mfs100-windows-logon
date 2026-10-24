using System;
using System.ServiceProcess;
using System.Threading;

namespace MFS100BridgeSvc
{
    public class BridgeService : ServiceBase
    {
        private WorkerHost? _workerHost;
        private PipeServer? _pipeServer;

        public const string ServiceNameIdentifier = "MFS100BridgeSvc";
        public static volatile bool IsSystemSuspending = false;

        public BridgeService()
        {
            ServiceName = ServiceNameIdentifier;
            CanStop = true;
            CanShutdown = true;
            CanHandlePowerEvent = true;
            AutoLog = true;
        }

        protected override bool OnPowerEvent(PowerBroadcastStatus powerStatus)
        {
            ServiceLog.Info($"Power event received: {powerStatus}");
            if (powerStatus == PowerBroadcastStatus.Suspend || (int)powerStatus == 0x0004)
            {
                ServiceLog.Info("System is suspending/sleeping. Signaling sensor LED to turn OFF and pause capture...");
                IsSystemSuspending = true;
                try
                {
                    using var suspendEvent = EventWaitHandle.OpenExisting(@"Global\MFS100_SuspendCapture");
                    suspendEvent.Set();
                }
                catch (Exception ex)
                {
                    ServiceLog.Warn($"Could not signal suspend event: {ex.Message}");
                }
            }
            else if (powerStatus == PowerBroadcastStatus.ResumeAutomatic ||
                     powerStatus == PowerBroadcastStatus.ResumeSuspend ||
                     (int)powerStatus == 0x0012 || (int)powerStatus == 0x0007)
            {
                ServiceLog.Info("System resumed from sleep. Signaling worker to turn sensor LED back ON...");
                IsSystemSuspending = false;
                try
                {
                    using var resumeEvent = EventWaitHandle.OpenExisting(@"Global\MFS100_ResumeCapture");
                    resumeEvent.Set();
                }
                catch (Exception ex)
                {
                    ServiceLog.Warn($"Could not signal resume event: {ex.Message}");
                }
            }
            return base.OnPowerEvent(powerStatus);
        }

        protected override void OnStart(string[] args)
        {
            ServiceLog.Init();
            ServiceLog.Info($"=== {ServiceNameIdentifier} Starting ===");

            try
            {
                _workerHost = new WorkerHost();
                _pipeServer = new PipeServer(_workerHost);
                _pipeServer.Start();
                ServiceLog.Info($"{ServiceNameIdentifier} started successfully.");
            }
            catch (Exception ex)
            {
                ServiceLog.Error($"Fatal error starting service: {ex}");
                throw;
            }
        }

        protected override void OnStop()
        {
            ServiceLog.Info($"=== {ServiceNameIdentifier} Stopping ===");

            try
            {
                _pipeServer?.Stop();
                _pipeServer?.Dispose();
                _pipeServer = null;

                _workerHost?.Dispose();
                _workerHost = null;

                ServiceLog.Info($"{ServiceNameIdentifier} stopped cleanly.");
            }
            catch (Exception ex)
            {
                ServiceLog.Error($"Error stopping service: {ex}");
            }
        }

        protected override void OnShutdown()
        {
            OnStop();
            base.OnShutdown();
        }

        public void StartInteractive(string[] args)
        {
            OnStart(args);
        }

        public void StopInteractive()
        {
            OnStop();
        }
    }
}
