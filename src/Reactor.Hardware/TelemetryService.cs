using System.Diagnostics;
using Reactor.Core.Configuration;
using Reactor.Core.Diagnostics;
using Reactor.Core.Metrics;
using Reactor.Core.Telemetry;

namespace Reactor.Hardware;

/// <summary>
/// Owns the polling loop. Everything - opening the driver, refreshing sensors,
/// building the frame - happens on one dedicated background thread, so the UI
/// thread never touches hardware and never blocks on it.
/// </summary>
public sealed class TelemetryService : IDisposable
{
    private readonly ISensorSource _source;
    private readonly TelemetryProcessor _processor;
    private readonly AppConfig _config;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _started = new(false);

    /// <summary>Transient hiccups are tolerated; a persistent fault blanks the board.</summary>
    private const int FailuresBeforeBlanking = 2;

    private Thread? _thread;
    private bool _disposed;

    public TelemetryService(ISensorSource source, TelemetryProcessor processor, AppConfig config)
    {
        _source = source;
        _processor = processor;
        _config = config;
    }

    /// <summary>Raised on the polling thread once per interval.</summary>
    public event Action<TelemetryFrame>? FrameProduced;

    /// <summary>Raised once when the source is open and identity is known.</summary>
    public event Action<ISensorSource>? SourceReady;

    /// <summary>Raised if the source could not be opened at all.</summary>
    public event Action<Exception>? Failed;

    public void Start()
    {
        if (_thread is not null) return;

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "reactor-telemetry",
            Priority = ThreadPriority.BelowNormal
        };
        _thread.Start();
    }

    private void Run()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            _source.Open();
            Log.Info($"Sensor source opened in {sw.ElapsedMilliseconds} ms");
            SourceReady?.Invoke(_source);
            _started.Set();
        }
        catch (Exception ex)
        {
            Log.Error("Sensor source failed to open", ex);
            Failed?.Invoke(ex);
            _started.Set();
            return;
        }

        var token = _cts.Token;
        var interval = _config.PollIntervalMs;
        var timer = Stopwatch.StartNew();
        var consecutiveFailures = 0;

        while (!token.IsCancellationRequested)
        {
            timer.Restart();

            try
            {
                var frame = _processor.Process(_source.Poll());
                FrameProduced?.Invoke(frame);
                consecutiveFailures = 0;
            }
            catch (Exception ex)
            {
                // A bad poll costs one frame, never the session.
                consecutiveFailures++;
                Log.Error($"Poll cycle failed ({consecutiveFailures} in a row)", ex);

                // Once failures stop looking transient, publish an empty frame.
                // Leaving the last good numbers on screen would be worse than
                // showing nothing: the board would look live while reporting
                // values that are minutes old.
                if (consecutiveFailures >= FailuresBeforeBlanking)
                {
                    try
                    {
                        FrameProduced?.Invoke(
                            _processor.Process(MetricsSnapshot.Unavailable(DateTime.UtcNow)));
                    }
                    catch (Exception inner)
                    {
                        Log.Error("Failed to publish an unavailable frame", inner);
                    }
                }
            }

            // Subtract the work we just did so the cadence stays honest even if
            // a refresh takes a while.
            var wait = interval - (int)timer.ElapsedMilliseconds;
            if (wait > 0 && token.WaitHandle.WaitOne(wait)) break;
        }

        Log.Info("Telemetry loop stopped");
    }

    /// <summary>Blocks until the source has opened (or failed). Startup only.</summary>
    public bool WaitForStart(TimeSpan timeout) => _started.Wait(timeout);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        // Give the loop a moment to leave the driver cleanly before closing it.
        _thread?.Join(TimeSpan.FromSeconds(2));
        _source.Dispose();
        _cts.Dispose();
        _started.Dispose();
    }
}
