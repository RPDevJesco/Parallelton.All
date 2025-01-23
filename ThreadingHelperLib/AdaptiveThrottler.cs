using System.Collections.Concurrent;
using System.Diagnostics;

namespace ThreadingHelpers
{
    /// <summary>
    /// Provides adaptive throttling based on system metrics and processing performance
    /// </summary>
    public class AdaptiveThrottler : IDisposable
    {
        private readonly ConcurrentQueue<TimeSpan> _processingTimes;
        private readonly SystemMetrics _systemMetrics;
        private readonly int _samplingSize;
        private readonly double _targetCpuPercent;
        private readonly double _targetMemoryPercent;
        private readonly TimeSpan _minDelay;
        private readonly TimeSpan _maxDelay;
        private TimeSpan _currentDelay;
        private readonly object _updateLock = new object();
        private readonly ConcurrentDictionary<string, Metric> _metrics = new();

        public AdaptiveThrottler(
            int samplingSize = 100,
            double targetCpuPercent = 70,
            double targetMemoryPercent = 80,
            TimeSpan? minDelay = null,
            TimeSpan? maxDelay = null)
        {
            _samplingSize = samplingSize;
            _targetCpuPercent = targetCpuPercent;
            _targetMemoryPercent = targetMemoryPercent;
            _minDelay = minDelay ?? TimeSpan.FromMilliseconds(0);
            _maxDelay = maxDelay ?? TimeSpan.FromMilliseconds(1000);
            _currentDelay = _minDelay;
            _processingTimes = new ConcurrentQueue<TimeSpan>();
            _systemMetrics = new SystemMetrics();
        }

        /// <summary>
        /// Current throttling metrics
        /// </summary>
        public class Metric
        {
            public double Value { get; set; }
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// Executes an operation with adaptive throttling
        /// </summary>
        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            try
            {
                // Apply current throttling delay
                if (_currentDelay > TimeSpan.Zero)
                {
                    await Task.Delay(_currentDelay, cancellationToken);
                }

                // Execute the operation
                await operation();
            }
            finally
            {
                // Record processing time
                sw.Stop();
                RecordProcessingTime(sw.Elapsed);

                // Adjust throttling based on metrics
                AdjustThrottling();
            }
        }

        private void RecordProcessingTime(TimeSpan processingTime)
        {
            _processingTimes.Enqueue(processingTime);
            while (_processingTimes.Count > _samplingSize)
            {
                _processingTimes.TryDequeue(out _);
            }

            // Record metric
            _metrics.AddOrUpdate(
                "ProcessingTime",
                new Metric { Value = processingTime.TotalMilliseconds, Timestamp = DateTime.UtcNow },
                (_, __) => new Metric { Value = processingTime.TotalMilliseconds, Timestamp = DateTime.UtcNow }
            );
        }

        private void AdjustThrottling()
        {
            lock (_updateLock)
            {
                var cpuUsage = _systemMetrics.GetCpuUsage();
                var memoryUsage = _systemMetrics.GetMemoryUsage();
                var avgProcessingTime = CalculateAverageProcessingTime();

                // Record metrics
                _metrics.AddOrUpdate(
                    "CpuUsage",
                    new Metric { Value = cpuUsage, Timestamp = DateTime.UtcNow },
                    (_, __) => new Metric { Value = cpuUsage, Timestamp = DateTime.UtcNow }
                );

                _metrics.AddOrUpdate(
                    "MemoryUsage",
                    new Metric { Value = memoryUsage, Timestamp = DateTime.UtcNow },
                    (_, __) => new Metric { Value = memoryUsage, Timestamp = DateTime.UtcNow }
                );

                // Adjust delay based on CPU and memory usage
                if (cpuUsage > _targetCpuPercent || memoryUsage > _targetMemoryPercent)
                {
                    // Increase delay when resource usage is too high
                    _currentDelay = TimeSpan.FromMilliseconds(
                        Math.Min(_currentDelay.TotalMilliseconds * 1.5, _maxDelay.TotalMilliseconds));
                }
                else if (cpuUsage < _targetCpuPercent * 0.8 && memoryUsage < _targetMemoryPercent * 0.8)
                {
                    // Decrease delay when resource usage is low
                    _currentDelay = TimeSpan.FromMilliseconds(
                        Math.Max(_currentDelay.TotalMilliseconds * 0.8, _minDelay.TotalMilliseconds));
                }

                // Further adjust based on processing times trend
                if (avgProcessingTime.HasValue)
                {
                    var processingTrend = CalculateProcessingTrend();
                    if (processingTrend > 1.2) // Processing times are increasing
                    {
                        _currentDelay = TimeSpan.FromMilliseconds(
                            Math.Min(_currentDelay.TotalMilliseconds * 1.2, _maxDelay.TotalMilliseconds));
                    }
                    else if (processingTrend < 0.8) // Processing times are decreasing
                    {
                        _currentDelay = TimeSpan.FromMilliseconds(
                            Math.Max(_currentDelay.TotalMilliseconds * 0.9, _minDelay.TotalMilliseconds));
                    }
                }

                // Record current delay metric
                _metrics.AddOrUpdate(
                    "CurrentDelay",
                    new Metric { Value = _currentDelay.TotalMilliseconds, Timestamp = DateTime.UtcNow },
                    (_, __) => new Metric { Value = _currentDelay.TotalMilliseconds, Timestamp = DateTime.UtcNow }
                );
            }
        }

        private double? CalculateAverageProcessingTime()
        {
            if (_processingTimes.IsEmpty)
                return null;

            var sum = TimeSpan.Zero;
            var count = 0;

            foreach (var time in _processingTimes)
            {
                sum += time;
                count++;
            }

            return count > 0 ? sum.TotalMilliseconds / count : null;
        }

        private double CalculateProcessingTrend()
        {
            var times = _processingTimes.ToArray();
            if (times.Length < 10)
                return 1.0;

            var recentAvg = times.Skip(times.Length / 2)
                .Average(t => t.TotalMilliseconds);
            var oldAvg = times.Take(times.Length / 2)
                .Average(t => t.TotalMilliseconds);

            return oldAvg > 0 ? recentAvg / oldAvg : 1.0;
        }

        /// <summary>
        /// Gets the current metrics for monitoring
        /// </summary>
        public IReadOnlyDictionary<string, Metric> GetCurrentMetrics() => _metrics;

        public void Dispose()
        {
            // Cleanup if needed
        }
    }

    // Extension to ParallelFlow to support adaptive throttling
    public static class AdaptiveThrottlingExtensions
    {
        public static ParallelFlow<T> WithAdaptiveThrottling<T>(
            this ParallelFlow<T> flow,
            double targetCpuPercent = 70,
            double targetMemoryPercent = 80,
            TimeSpan? minDelay = null,
            TimeSpan? maxDelay = null)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));
        
            var throttler = new AdaptiveThrottler(
                targetCpuPercent: targetCpuPercent,
                targetMemoryPercent: targetMemoryPercent,
                minDelay: minDelay,
                maxDelay: maxDelay
            );

            return flow.SetAdaptiveThrottler(throttler);
        }
    }
}