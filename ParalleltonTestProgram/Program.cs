using AsyncHelpers;
using ThreadingHelpers;
using System.Collections.Concurrent;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            var metrics = new MetricsAggregator();
            await ProcessLogsDemo(metrics);
            metrics.PrintSummary();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Application error: {ex.Message}");
        }
    }

    static async Task ProcessLogsDemo(MetricsAggregator metrics)
    {
        using var cts = new CancellationTokenSource();
        var eventAggregator = new AsyncEventAggregator();
        var circuitBreaker = new CircuitBreaker(failureThreshold: 3);
        var adaptiveThrottler = new AdaptiveThrottler(targetCpuPercent: 70, targetMemoryPercent: 80);

        eventAggregator.Subscribe<LogProcessingMetric>(async metric => {
            metrics.AddMetric(metric);
            if (metric.ProcessingTime > 90)
            {
                Console.WriteLine($"ALERT: High processing time {metric.ProcessingTime:F2}ms for {metric.EntryId}");
            }
        });

        var batchProcessor = new BatchProcessor<LogEntry>(
            processBatch: async entries => {
                await ProcessLogBatch(entries, eventAggregator, circuitBreaker, adaptiveThrottler, cts.Token);
            },
            batchSize: 100,
            maxWaitTime: TimeSpan.FromSeconds(5)
        );

        var logGenerator = new LogGenerator();
        var periodicTask = PeriodicTask.Run(async () => {
            try 
            {
                if (!cts.Token.IsCancellationRequested)
                {
                    var entry = logGenerator.GenerateLogEntry();
                    await batchProcessor.AddItemAsync(entry);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating log: {ex.Message}");
            }
        }, TimeSpan.FromMilliseconds(100));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when canceling
        }
        finally
        {
            cts.Cancel();
            try
            {
                await periodicTask.StopAndWait();
                await batchProcessor.CompleteAsync();
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
        }
    }

    static async Task ProcessLogBatch(
        IList<LogEntry> entries,
        AsyncEventAggregator eventAggregator,
        CircuitBreaker circuitBreaker,
        AdaptiveThrottler adaptiveThrottler,
        CancellationToken cancellationToken)
    {
        await entries.ParallelFlow()
            .WithMaxParallel(Environment.ProcessorCount)
            .WithRateLimit(maxOperations: 50, intervalMs: 1000)
            .WithRetry(count: 3)
            .OnItemError((entry, ex) => {
                if (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"ERROR: Failed to process {entry.Id}: {ex.Message}");
                }
            })
            .ForEachAsync(async entry => {
                if (!cancellationToken.IsCancellationRequested)
                {
                    await adaptiveThrottler.ExecuteAsync(async () => {
                        await circuitBreaker.ExecuteAsync(async () => {
                            await SimulateProcessing(cancellationToken);
                            await eventAggregator.PublishAsync(new LogProcessingMetric {
                                EntryId = entry.Id,
                                ProcessingTime = Random.Shared.NextDouble() * 100,
                                Severity = entry.Severity,
                                Timestamp = DateTime.UtcNow
                            });
                            return true;
                        });
                    }, cancellationToken);
                }
            }, cancellationToken);
    }

    static async Task SimulateProcessing(CancellationToken cancellationToken)
    {
        await Task.Delay(Random.Shared.Next(50, 150), cancellationToken);
        if (Random.Shared.NextDouble() < 0.05)
        {
            throw new Exception("Simulated processing error");
        }
    }
}

public class MetricsAggregator
{
    private readonly ConcurrentDictionary<string, List<LogProcessingMetric>> _metricsBySeverity = new();
    private readonly ConcurrentQueue<(DateTime time, double processingTime)> _timeSeriesData = new();
    private readonly ConcurrentDictionary<string, MovingWindow> _movingWindows = new();
    private readonly ConcurrentDictionary<string, AlertGroup> _alertGroups = new();
    private int _totalProcessed;
    private readonly LinkedList<DateTime> _processingTimes = new();
    private double _apdexTarget = 80; // ms
    private readonly object _lock = new object();
    private const int WindowSizeSeconds = 60;

    private double CalculateApdexScore()
    {
        var satisfied = _timeSeriesData.Count(x => x.processingTime <= _apdexTarget);
        var tolerating = _timeSeriesData.Count(x => x.processingTime <= _apdexTarget * 4 && x.processingTime > _apdexTarget);
        var total = _timeSeriesData.Count;
        return (satisfied + tolerating / 2.0) / total;
    }

    private double CalculateErrorRate() => 
        _metricsBySeverity.GetValueOrDefault("ERROR", new List<LogProcessingMetric>()).Count * 100.0 / _totalProcessed;


        private class AlertGroup
    {
        public int Count { get; set; }
        public double MaxProcessingTime { get; set; }
        public double TotalProcessingTime { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }
        

    private void PrintAlertSummary(List<KeyValuePair<string, AlertGroup>> alerts, LogProcessingMetric trigger)
    {
        Console.WriteLine("\nPERFORMANCE ALERT:");
        foreach (var group in alerts)
        {
            var severity = group.Key.Split('-')[0];
            var avg = group.Value.TotalProcessingTime / group.Value.Count;
            var rate = group.Value.Count * 60.0 / (DateTime.UtcNow - group.Value.FirstSeen).TotalSeconds;
            Console.WriteLine(
                $"{severity,-5}: {group.Value.Count,3} alerts ({rate:F1}/min) | " +
                $"Max={group.Value.MaxProcessingTime:F1}ms | " +
                $"Avg={avg:F1}ms");
        }
        Console.WriteLine($"Latest: {trigger.ProcessingTime:F1}ms {trigger.Severity} | " +
                         $"Throughput: {CalculateThroughput():F1} ops/sec");
    }
    
    public MetricsAggregator()
    {
        foreach (var severity in new[] { "INFO", "WARN", "ERROR", "DEBUG" })
        {
            _movingWindows[severity] = new MovingWindow(TimeSpan.FromSeconds(WindowSizeSeconds));
        }
    }

    public void AddMetric(LogProcessingMetric metric)
    {
        _metricsBySeverity.GetOrAdd(metric.Severity, _ => new List<LogProcessingMetric>()).Add(metric);
        _timeSeriesData.Enqueue((metric.Timestamp, metric.ProcessingTime));
        var window = _movingWindows[metric.Severity];
        window.Add(metric.ProcessingTime);

        lock (_lock)
        {
            _totalProcessed++;
            _processingTimes.AddLast(DateTime.UtcNow);
            CleanOldProcessingTimes();

            if (IsAnomaly(metric.ProcessingTime, window))
            {
                HandleAlert(metric);
            }
        }
    }

    private void CleanOldProcessingTimes()
    {
        var threshold = DateTime.UtcNow.AddSeconds(-WindowSizeSeconds);
        while (_processingTimes.First != null && _processingTimes.First.Value < threshold)
        {
            _processingTimes.RemoveFirst();
        }
    }

    private bool IsAnomaly(double value, MovingWindow window)
    {
        if (window.Count < 10) return value > 90;
        var mean = window.Average;
        var stdDev = window.StandardDeviation;
        return value > mean + (2 * stdDev) || value > 90;
    }

    private void HandleAlert(LogProcessingMetric metric)
    {
        var now = DateTime.UtcNow;
        var groupKey = $"{metric.Severity}-{now.Minute}";

        _alertGroups.AddOrUpdate(groupKey,
            _ => new AlertGroup
            {
                Count = 1,
                MaxProcessingTime = metric.ProcessingTime,
                TotalProcessingTime = metric.ProcessingTime,
                FirstSeen = now,
                LastSeen = now
            },
            (_, group) =>
            {
                group.Count++;
                group.MaxProcessingTime = Math.Max(group.MaxProcessingTime, metric.ProcessingTime);
                group.TotalProcessingTime += metric.ProcessingTime;
                group.LastSeen = now;
                return group;
            });

        var activeAlerts = _alertGroups
            .Where(g => now - g.Value.LastSeen <= TimeSpan.FromMinutes(1))
            .OrderByDescending(g => g.Value.Count)
            .ToList();

        if (activeAlerts.Sum(g => g.Value.Count) % 5 == 0)
        {
            PrintAlertSummary(activeAlerts, metric);
        }
    }

    private void PrintAlertSummary(LogProcessingMetric trigger)
    {
        var activeAlerts = _alertGroups
            .Where(g => DateTime.UtcNow - g.Value.LastSeen <= TimeSpan.FromMinutes(1))
            .OrderByDescending(g => g.Value.Count)
            .ToList();

        var totalAlerts = activeAlerts.Sum(g => g.Value.Count);
        if (totalAlerts % 5 == 0) // Print summary every 5 alerts
        {
            Console.WriteLine("\nALERT SUMMARY:");
            foreach (var group in activeAlerts)
            {
                var severity = group.Key.Split('-')[0];
                var avg = group.Value.TotalProcessingTime / group.Value.Count;
                Console.WriteLine(
                    $"- {severity}: {group.Value.Count} alerts | " +
                    $"Max={group.Value.MaxProcessingTime:F2}ms | " +
                    $"Avg={avg:F2}ms");
            }
            Console.WriteLine($"Latest: {trigger.ProcessingTime:F2}ms ({trigger.Severity})");
        }
    }

    public void PrintSummary()
    {
        var throughput = CalculateThroughput();
        var apdexScore = CalculateApdexScore();
        
        Console.WriteLine($"\nPERFORMANCE SUMMARY ({DateTime.UtcNow:HH:mm:ss}):");
        Console.WriteLine($"Throughput: {throughput:F1} ops/sec | APDEX: {apdexScore:F2} | Error Rate: {CalculateErrorRate():F1}%");

        Console.WriteLine($"\nLATENCY STATS ({WindowSizeSeconds}s window):");
        foreach (var severity in _movingWindows.Keys.OrderByDescending(k => _movingWindows[k].Count))
        {
            var window = _movingWindows[severity];
            if (window.Count > 0)
            {
                Console.WriteLine(
                    $"{severity,-5}: p50={window.GetPercentile(50):F1}ms | " +
                    $"p95={window.GetPercentile(95):F1}ms | " +
                    $"p99={window.GetPercentile(99):F1}ms | " +
                    $"n={window.Count}");
            }
        }
    }

    private double CalculateThroughput()
    {
        if (_processingTimes.Count < 2) return 0;
        var timeSpan = _processingTimes.Last.Value - _processingTimes.First.Value;
        return _processingTimes.Count / timeSpan.TotalSeconds;
    }

    private class MovingWindow
    {
        private readonly ConcurrentQueue<(DateTime timestamp, double value)> _values = new();
        private readonly TimeSpan _windowSize;
        private readonly object _lock = new object();

        public MovingWindow(TimeSpan windowSize) => _windowSize = windowSize;

        public void Add(double value)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _values.Enqueue((now, value));
                while (_values.TryPeek(out var oldest) && now - oldest.timestamp > _windowSize)
                {
                    _values.TryDequeue(out _);
                }
            }
        }

        public double GetPercentile(int percentile)
        {
            var values = _values.Select(x => x.value).OrderBy(x => x).ToList();
            var index = (int)Math.Ceiling(percentile / 100.0 * values.Count) - 1;
            return values[Math.Max(0, index)];
        }

        public double StandardDeviation
        {
            get
            {
                var values = _values.Select(x => x.value).ToList();
                if (values.Count < 2) return 0;
                var avg = values.Average();
                var sumOfSquares = values.Sum(x => Math.Pow(x - avg, 2));
                return Math.Sqrt(sumOfSquares / (values.Count - 1));
            }
        }

        public double Average => _values.Average(x => x.value);
        public int Count => _values.Count;
    }
}

public record LogEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Message { get; init; } = "";
    public string Severity { get; init; } = "INFO";
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public record LogProcessingMetric
{
    public string EntryId { get; init; } = "";
    public double ProcessingTime { get; init; }
    public string Severity { get; init; } = "";
    public DateTime Timestamp { get; init; }
}

public class LogGenerator
{
    private static readonly string[] Severities = { "INFO", "WARN", "ERROR", "DEBUG" };
    private static readonly string[] Messages = {
        "User logged in",
        "Database connection failed",
        "Cache miss",
        "Request processed",
        "Payment completed",
        "API rate limit exceeded"
    };

    public LogEntry GenerateLogEntry()
    {
        return new LogEntry
        {
            Message = Messages[Random.Shared.Next(Messages.Length)],
            Severity = Severities[Random.Shared.Next(Severities.Length)]
        };
    }
}