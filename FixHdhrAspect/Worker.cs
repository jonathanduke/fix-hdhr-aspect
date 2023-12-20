// Template: https://learn.microsoft.com/en-us/dotnet/core/extensions/windows-service?pivots=dotnet-7-0#rewrite-the-worker-class

namespace JonathanDuke.FixHdhrAspect;

public sealed class Worker : BackgroundService
{
    private readonly ProxyService _proxyService;
    private readonly ILogger<Worker> _logger;

    public Worker(
        ProxyService proxyService,
        ILogger<Worker> logger)
            => (_proxyService, _logger) = (proxyService, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int exitCode = 0;
        Task[]? listeners = null;

        try
        {
#if DEBUG
            _logger.LogDebug("{time:o}: Worker started.", DateTimeOffset.Now);
#endif
            listeners = [
                _proxyService.StartCaptureAsync(stoppingToken),
                _proxyService.StartWebAsync(stoppingToken),
            ];

            while (!stoppingToken.IsCancellationRequested && !listeners.Any(l => l.IsCompleted))
            {
#if DEBUG
                //_logger.LogDebug("{time:o}: Worker running.", DateTimeOffset.Now);
#endif
                await Task.Delay(1000, stoppingToken);
            }
        }
        catch (TaskCanceledException)
        {
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...
#if DEBUG
            _logger.LogDebug("{time:o}: Cancel signal received.", DateTimeOffset.Now);
#endif
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Message}", ex.Message);
            exitCode = 1;
        }
        finally
        {
            try
            {
                if (listeners != null)
                {
                    try
                    {
                        // give any tasks that are still running a little extra time to gracefully shut down
                        Task.WaitAll(listeners.Where(l => !l.IsCompleted).ToArray(), 5000, CancellationToken.None);
                    }
                    catch { }

                    int stillRunning = listeners.Where(l => !l.IsCompleted).Count();

                    if (stillRunning > 0)
                    {
                        _logger.LogWarning("Failed to shut down all listeners in a timely manner. ({count})", stillRunning);
                    }

                    foreach (var listener in listeners)
                    {
                        if (listener.IsFaulted)
                        {
                            foreach (var ex in listener.Exception!.InnerExceptions)
                            {
                                _logger.LogError(ex, "{Message}", ex.Message);
                            }
                        }
                    }
                }
#if DEBUG
                _logger.LogDebug("{time:o}: Worker stopped.", DateTimeOffset.Now);
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                exitCode = 2;
            }

            if (exitCode != 0)
            {
                // Terminates this process and returns an exit code to the operating system.
                // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
                // performs one of two scenarios:
                // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
                // 2. When set to "StopHost": will cleanly stop the host, and log errors.
                //
                // In order for the Windows Service Management system to leverage configured
                // recovery options, we need to terminate the process with a non-zero exit code.
                Environment.Exit(exitCode);
            }
        }
    }
}
