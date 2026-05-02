using System.Diagnostics;
using NexusRemotePC.Media;

namespace NexusRemotePC;

public static class ShutdownScheduler
{
    private static readonly object Sync = new();
    private static CancellationTokenSource? _monitorCts;
    private static System.Threading.Timer? _timer;
    private static int _remainingVideoTransitions;
    private static string? _lastVideoSignature;

    public static void ScheduleTimer(int seconds)
    {
        lock (Sync)
        {
            CancelInternal();
            _timer = new System.Threading.Timer(_ => ExecuteShutdown(), null, TimeSpan.FromSeconds(seconds), Timeout.InfiniteTimeSpan);
            AppLogger.Info($"Таймер выключения запущен на {seconds} сек.");
        }
    }

    public static void ScheduleAfterVideoTransitions(int count)
    {
        lock (Sync)
        {
            CancelInternal();
            _remainingVideoTransitions = Math.Max(1, count);
            _lastVideoSignature = null;
            _monitorCts = new CancellationTokenSource();
            _ = Task.Run(() => MonitorVideoTransitionsAsync(_monitorCts.Token));
            AppLogger.Info($"Выключение после {count} видео запланировано.");
        }
    }

    public static void Cancel()
    {
        lock (Sync)
        {
            CancelInternal();
            AppLogger.Info("Планировщик выключения отменён.");
        }
    }

    private static async Task MonitorVideoTransitionsAsync(CancellationToken cancellationToken)
    {
        var broker = MediaBroker.CreateDefault();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var snapshot = await broker.GetSnapshotAsync(cancellationToken);
                var active = snapshot.Sources.FirstOrDefault(source => source.SourceId == snapshot.ActiveSourceId)
                             ?? snapshot.Sources.FirstOrDefault();
                var signature = active != null
                                && string.Equals(active.MediaKind, "video", StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrWhiteSpace(active.Title)
                    ? $"{active.AppId}|{active.Title}|{active.Artist}"
                    : null;

                lock (Sync)
                {
                    if (signature != null && _lastVideoSignature == null)
                    {
                        _lastVideoSignature = signature;
                    }
                    else if (signature != null
                             && _lastVideoSignature != null
                             && !string.Equals(signature, _lastVideoSignature, StringComparison.OrdinalIgnoreCase))
                    {
                        _remainingVideoTransitions--;
                        _lastVideoSignature = signature;
                        AppLogger.Info($"Видео завершилось, осталось до выключения: {_remainingVideoTransitions}");
                        if (_remainingVideoTransitions <= 0)
                        {
                            CancelInternal();
                            ExecuteShutdown();
                            return;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Video shutdown monitor: {ex.Message}");
            }

            await Task.Delay(1000, cancellationToken);
        }
    }

    private static void ExecuteShutdown()
    {
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", "/s /t 0")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLogger.Error("Не удалось выполнить shutdown.", ex);
        }
    }

    private static void CancelInternal()
    {
        _timer?.Dispose();
        _timer = null;
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
        _remainingVideoTransitions = 0;
        _lastVideoSignature = null;
    }
}
