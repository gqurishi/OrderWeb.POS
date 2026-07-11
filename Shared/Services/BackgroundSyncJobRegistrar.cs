using Microsoft.Extensions.DependencyInjection;

namespace POS_in_NET.Services;

public static class BackgroundSyncJobRegistrar
{
    public static async Task RegisterDefaultJobsAsync(IServiceProvider services)
    {
        var manager = services.GetRequiredService<BackgroundSyncManager>();

        RegisterTerminalHealthJob(manager, services.GetRequiredService<TerminalHealthService>());

        if (TerminalRoleService.CanRunMotherJobs)
        {
            RegisterDatabaseBackupJob(manager, services.GetRequiredService<DatabaseBackupService>());
            RegisterCleanupJob(manager, services.GetRequiredService<CleanupSchedulerService>());
            RegisterReportJob(manager, services.GetRequiredService<ReportSchedulerService>());
        }

        if (TerminalConfigurationService.IsConfigured)
        {
            await RegisterPrinterJobsAsync(manager, services);
            RegisterLiveUpdateJob(manager);
            RegisterCloudJobs(manager, services);
        }
    }

    private static void RegisterTerminalHealthJob(
        BackgroundSyncManager manager,
        TerminalHealthService terminalHealthService)
    {
        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "terminal-health-heartbeat",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(30),
                IdleInterval = TimeSpan.FromSeconds(30),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(5),
                RequiredResources = BackgroundSyncResources.Database,
                TerminalScope = BackgroundSyncTerminalScope.ChildSafe,
                CanRunDuringPaymentOrOrderEntry = true
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await terminalHealthService.UpdateCurrentTerminalAsync();
                return terminalHealthService.LastHeartbeatStatus.Equals("Online", StringComparison.OrdinalIgnoreCase)
                    ? BackgroundSyncRunResult.Completed("Terminal heartbeat updated.")
                    : BackgroundSyncRunResult.Failed(terminalHealthService.LastHeartbeatStatus);
            });
    }

    private static void RegisterDatabaseBackupJob(
        BackgroundSyncManager manager,
        DatabaseBackupService databaseBackupService)
    {
        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "database-backup-scheduler",
                Priority = BackgroundSyncPriority.Low,
                NormalInterval = TimeSpan.FromHours(1),
                IdleInterval = TimeSpan.FromHours(1),
                FailureBackoff = TimeSpan.FromMinutes(30),
                InitialDelay = TimeSpan.FromSeconds(20),
                RequiredResources = BackgroundSyncResources.Database,
                TerminalScope = BackgroundSyncTerminalScope.MotherOnly,
                CanRunDuringPaymentOrOrderEntry = false,
                CanRunWhileUserActive = false
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await databaseBackupService.RunScheduledBackupIfNeededAsync();
                return result.Success
                    ? BackgroundSyncRunResult.Completed(result.Message)
                    : BackgroundSyncRunResult.Failed(result.Message);
            });
    }

    private static async Task RegisterPrinterJobsAsync(
        BackgroundSyncManager manager,
        IServiceProvider services)
    {
        var printerDbService = services.GetRequiredService<NetworkPrinterDatabaseService>();
        var cashDrawerService = services.GetRequiredService<CashDrawerService>();
        var printingPolicyService = services.GetRequiredService<PrintingPolicyService>();
        var printerHealthService = services.GetRequiredService<PrinterHealthService>();
        var printQueueService = services.GetRequiredService<NetworkPrintQueueService>();

        await printerDbService.EnsureTablesExistAsync();
        await cashDrawerService.EnsureTableExistsAsync();
        await printingPolicyService.EnsureDefaultsAsync();
        await printQueueService.EnsureTableExistsAsync();

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "printer-health-check",
                Priority = BackgroundSyncPriority.Low,
                NormalInterval = TimeSpan.FromMinutes(5),
                IdleInterval = TimeSpan.FromSeconds(45),
                FailureBackoff = TimeSpan.FromMinutes(2),
                InitialDelay = TimeSpan.FromSeconds(20),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Network |
                                    BackgroundSyncResources.Printer,
                TerminalScope = BackgroundSyncTerminalScope.ChildSafe,
                CanRunDuringPaymentOrOrderEntry = false,
                CanRunWhileUserActive = false
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await printerHealthService.CheckAllPrintersAsync();
                return BackgroundSyncRunResult.Completed(
                    $"{printerHealthService.OnlinePrinters}/{printerHealthService.TotalPrinters} printer(s) online.");
            });

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "network-print-queue",
                Priority = BackgroundSyncPriority.Critical,
                NormalInterval = TimeSpan.FromSeconds(5),
                IdleInterval = TimeSpan.FromSeconds(5),
                FailureBackoff = TimeSpan.FromSeconds(15),
                InitialDelay = TimeSpan.FromSeconds(2),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Network |
                                    BackgroundSyncResources.Printer,
                TerminalScope = BackgroundSyncTerminalScope.ChildSafe,
                CanRunDuringPaymentOrOrderEntry = true,
                CanRunWhileUserActive = true
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var queueOwnership = await printingPolicyService.CanProcessSharedQueueAsync();
                if (!queueOwnership.Allowed)
                {
                    return BackgroundSyncRunResult.Skip(queueOwnership.Reason);
                }

                await printQueueService.ProcessQueueAsync();
                return BackgroundSyncRunResult.Completed("Print queue checked.");
            });
    }

    private static void RegisterLiveUpdateJob(BackgroundSyncManager manager)
    {
        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "till-live-update-poll",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(5),
                IdleInterval = TimeSpan.FromSeconds(3),
                FailureBackoff = TimeSpan.FromSeconds(10),
                InitialDelay = TimeSpan.FromSeconds(3),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.ChildSafe,
                CanRunDuringPaymentOrOrderEntry = true,
                CanRunWhileUserActive = true
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DatabaseChangeMonitorService.PollOnceAsync();
                return BackgroundSyncRunResult.Completed("Till live updates checked.");
            });
    }

    private static void RegisterCloudJobs(
        BackgroundSyncManager manager,
        IServiceProvider services)
    {
        var connectionKeeper = services.GetRequiredService<OrderWebConnectionKeeperService>();
        var heartbeatService = services.GetRequiredService<HeartbeatService>();
        var giftCardQueueService = services.GetRequiredService<GiftCardActivationQueueService>();
        var offlineQueueService = services.GetRequiredService<OfflineQueueService>();
        var cloudOrderService = services.GetRequiredService<CloudOrderService>();
        var reservationSyncService = services.GetRequiredService<ReservationSyncService>();

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "orderweb-connection-health",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(90),
                IdleInterval = TimeSpan.FromSeconds(60),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(10),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.OnlineOrderMasterOnly,
                CanRunDuringPaymentOrOrderEntry = true,
                CanRunWhileUserActive = true
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await connectionKeeper.EnsureHealthyAsync();
                return connectionKeeper.Status.IsApiHealthy
                    ? BackgroundSyncRunResult.Completed(connectionKeeper.Status.StatusMessage)
                    : BackgroundSyncRunResult.Failed(connectionKeeper.Status.LastError ?? connectionKeeper.Status.StatusMessage);
            });

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "cloud-heartbeat",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(30),
                IdleInterval = TimeSpan.FromSeconds(30),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(12),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.OnlineOrderMasterOnly,
                CanRunDuringPaymentOrOrderEntry = true,
                CanRunWhileUserActive = true
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var success = !heartbeatService.IsRunning
                    ? await heartbeatService.StartAsync()
                    : await heartbeatService.SendHeartbeatAsync();

                if (!success)
                {
                    return BackgroundSyncRunResult.Failed(heartbeatService.LastHeartbeatStatus);
                }

                return heartbeatService.LastHeartbeatStatus.Equals("Online", StringComparison.OrdinalIgnoreCase)
                    ? BackgroundSyncRunResult.Completed(heartbeatService.LastHeartbeatStatus)
                    : BackgroundSyncRunResult.Skip(heartbeatService.LastHeartbeatStatus);
            });

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "gift-card-activate-flush",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(30),
                IdleInterval = TimeSpan.FromSeconds(30),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(15),
                RequiredResources = BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.OnlineOrderMasterOnly,
                CanRunDuringPaymentOrOrderEntry = false,
                CanRunWhileUserActive = false
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var pending = await giftCardQueueService.GetPendingCountAsync();
                if (pending == 0)
                {
                    return BackgroundSyncRunResult.Skip("No queued gift card activations.");
                }

                var result = await giftCardQueueService.FlushAsync();
                return result.Success
                    ? BackgroundSyncRunResult.Completed(result.Message)
                    : BackgroundSyncRunResult.Failed(result.Message);
            });

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "offline-api-queue-flush",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(30),
                IdleInterval = TimeSpan.FromSeconds(30),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(18),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.OnlineOrderMasterOnly,
                CanRunDuringPaymentOrOrderEntry = false,
                CanRunWhileUserActive = false
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await offlineQueueService.ProcessQueueAsync();
                return result.Failed == 0
                    ? BackgroundSyncRunResult.Completed($"Offline queue flushed {result.Sent} item(s).")
                    : BackgroundSyncRunResult.Failed($"Offline queue failed {result.Failed} item(s).");
            });

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "online-order-api-backup-poll",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(30),
                IdleInterval = TimeSpan.FromSeconds(15),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(15),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.OnlineOrderMasterOnly,
                CanRunDuringPaymentOrOrderEntry = true,
                CanRunWhileUserActive = true
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await cloudOrderService.RunBackupPollingOnceAsync();
                return result.Success
                    ? BackgroundSyncRunResult.Completed(result.Message)
                    : BackgroundSyncRunResult.Failed(result.Message);
            });

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "online-order-ack-retry",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(60),
                IdleInterval = TimeSpan.FromSeconds(60),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(30),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.OnlineOrderMasterOnly,
                CanRunDuringPaymentOrOrderEntry = true,
                CanRunWhileUserActive = true
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await cloudOrderService.RetryPendingAcksOnceAsync();
                return result.Success
                    ? BackgroundSyncRunResult.Completed(result.Message)
                    : BackgroundSyncRunResult.Failed(result.Message);
            });

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "reservation-inbound-poll",
                Priority = BackgroundSyncPriority.Important,
                NormalInterval = TimeSpan.FromSeconds(60),
                IdleInterval = TimeSpan.FromSeconds(30),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(20),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.OnlineOrderMasterOnly,
                CanRunDuringPaymentOrOrderEntry = false,
                CanRunWhileUserActive = false
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await reservationSyncService.SyncTodayAsync(includeCancelled: true);
                return result.Success
                    ? BackgroundSyncRunResult.Completed(result.Message)
                    : BackgroundSyncRunResult.Failed(result.Message);
            });

        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "reservation-maintenance",
                Priority = BackgroundSyncPriority.Normal,
                NormalInterval = TimeSpan.FromSeconds(60),
                IdleInterval = TimeSpan.FromSeconds(60),
                FailureBackoff = TimeSpan.FromSeconds(60),
                InitialDelay = TimeSpan.FromSeconds(45),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.OnlineOrderMasterOnly,
                CanRunDuringPaymentOrOrderEntry = false,
                CanRunWhileUserActive = false
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await reservationSyncService.RunMaintenanceOnceAsync();
                return BackgroundSyncRunResult.Completed(
                    $"Reservation maintenance uploaded {result.Uploaded}, ACKed {result.AcksSent}.");
            });
    }

    private static void RegisterCleanupJob(
        BackgroundSyncManager manager,
        CleanupSchedulerService cleanupSchedulerService)
    {
        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "database-cleanup-scheduler",
                Priority = BackgroundSyncPriority.Low,
                NormalInterval = TimeSpan.FromHours(1),
                IdleInterval = TimeSpan.FromHours(1),
                FailureBackoff = TimeSpan.FromMinutes(30),
                InitialDelay = TimeSpan.FromMinutes(5),
                RequiredResources = BackgroundSyncResources.Database,
                TerminalScope = BackgroundSyncTerminalScope.MotherOnly,
                CanRunDuringPaymentOrOrderEntry = false,
                CanRunWhileUserActive = false
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await cleanupSchedulerService.InitializeDatabaseAsync();
                await cleanupSchedulerService.CheckAndRunCleanupAsync();
                return BackgroundSyncRunResult.Completed("Database cleanup checked.");
            });
    }

    private static void RegisterReportJob(
        BackgroundSyncManager manager,
        ReportSchedulerService reportSchedulerService)
    {
        manager.RegisterJob(
            new BackgroundSyncJobDefinition
            {
                Name = "report-generation-scheduler",
                Priority = BackgroundSyncPriority.Low,
                NormalInterval = TimeSpan.FromHours(1),
                IdleInterval = TimeSpan.FromHours(1),
                FailureBackoff = TimeSpan.FromMinutes(30),
                InitialDelay = TimeSpan.FromSeconds(45),
                RequiredResources = BackgroundSyncResources.Database |
                                    BackgroundSyncResources.Cloud |
                                    BackgroundSyncResources.Api |
                                    BackgroundSyncResources.Network,
                TerminalScope = BackgroundSyncTerminalScope.MotherOnly,
                CanRunDuringPaymentOrOrderEntry = false,
                CanRunWhileUserActive = false
            },
            async cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await reportSchedulerService.CheckAndGenerateReportsAsync();
                return BackgroundSyncRunResult.Completed("Report scheduler checked.");
            });
    }
}
