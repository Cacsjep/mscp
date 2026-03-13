using System;
using System.ServiceProcess;
using System.Threading;
using WixToolset.Dtf.WindowsInstaller;

namespace InstallerCustomActions
{
    public static class ServiceActions
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

        [CustomAction]
        public static ActionResult EnsureServiceState(Session session)
        {
            try
            {
                var serviceName = session.CustomActionData["ServiceName"];
                var displayName = session.CustomActionData["DisplayName"];
                var operation = session.CustomActionData["Operation"];
                var timeout = GetTimeout(session);

                using (var service = TryGetService(serviceName, displayName))
                {
                    if (service == null)
                    {
                        session.Log($"[InstallerCustomActions] Service '{displayName}' ({serviceName}) is not installed. Continuing.");
                        return ActionResult.Success;
                    }

                    session.Log($"[InstallerCustomActions] Ensuring service '{displayName}' ({serviceName}) reaches state '{operation}' within {timeout.TotalSeconds:0} seconds.");

                    switch (operation)
                    {
                        case "Stop":
                            return StopService(session, service, displayName, timeout);
                        case "Start":
                            return StartService(session, service, displayName, timeout);
                        default:
                            session.Log($"[InstallerCustomActions] Unknown service operation '{operation}'.");
                            return ActionResult.Failure;
                    }
                }
            }
            catch (Exception ex)
            {
                session.Log($"[InstallerCustomActions] Service action failed: {ex}");
                return ActionResult.Failure;
            }
        }

        private static ActionResult StopService(Session session, ServiceController service, string displayName, TimeSpan timeout)
        {
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Stopped)
            {
                session.Log($"[InstallerCustomActions] Service '{displayName}' is already stopped.");
                return ActionResult.Success;
            }

            if (service.Status != ServiceControllerStatus.StopPending)
            {
                session.Log($"[InstallerCustomActions] Stopping service '{displayName}'.");
                service.Stop();
            }

            return WaitForStatus(session, service, displayName, ServiceControllerStatus.Stopped, timeout);
        }

        private static ActionResult StartService(Session session, ServiceController service, string displayName, TimeSpan timeout)
        {
            service.Refresh();
            if (service.Status == ServiceControllerStatus.Running)
            {
                session.Log($"[InstallerCustomActions] Service '{displayName}' is already running.");
                return ActionResult.Success;
            }

            if (service.Status != ServiceControllerStatus.StartPending)
            {
                session.Log($"[InstallerCustomActions] Starting service '{displayName}'.");
                service.Start();
            }

            return WaitForStatus(session, service, displayName, ServiceControllerStatus.Running, timeout);
        }

        private static ActionResult WaitForStatus(Session session, ServiceController service, string displayName, ServiceControllerStatus targetStatus, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            var nextStatusLog = DateTime.UtcNow;

            while (DateTime.UtcNow <= deadline)
            {
                service.Refresh();
                if (service.Status == targetStatus)
                {
                    session.Log($"[InstallerCustomActions] Service '{displayName}' reached state '{targetStatus}'.");
                    return ActionResult.Success;
                }

                if (DateTime.UtcNow >= nextStatusLog)
                {
                    session.Log($"[InstallerCustomActions] Service '{displayName}' current state: {service.Status}.");
                    nextStatusLog = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                }

                Thread.Sleep(PollInterval);
            }

            session.Log($"[InstallerCustomActions] Timed out waiting for '{displayName}' to reach state '{targetStatus}'.");
            return ActionResult.Failure;
        }

        private static ServiceController TryGetService(string serviceName, string displayName)
        {
            foreach (var service in ServiceController.GetServices())
            {
                if (!string.IsNullOrWhiteSpace(serviceName) &&
                    string.Equals(service.ServiceName, serviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return service;
                }

                if (!string.IsNullOrWhiteSpace(displayName) &&
                    string.Equals(service.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                {
                    return service;
                }

                service.Dispose();
            }

            return null;
        }

        private static TimeSpan GetTimeout(Session session)
        {
            if (int.TryParse(session.CustomActionData["TimeoutSeconds"], out var timeoutSeconds) && timeoutSeconds > 0)
            {
                return TimeSpan.FromSeconds(timeoutSeconds);
            }

            return DefaultTimeout;
        }
    }
}
