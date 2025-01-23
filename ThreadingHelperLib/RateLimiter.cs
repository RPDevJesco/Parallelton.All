using System.Collections.Concurrent;

namespace ThreadingHelpers
{
    /// <summary>
    /// Provides a thread-safe rate limiter for controlling concurrent operations
    /// </summary>
    public class RateLimiter
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxParallel;
        private readonly int _intervalMs;
        private readonly ConcurrentQueue<DateTime> _accessTimes;

        public RateLimiter(int maxParallel, int intervalMs)
        {
            _maxParallel = maxParallel;
            _intervalMs = intervalMs;
            _semaphore = new SemaphoreSlim(maxParallel);
            _accessTimes = new ConcurrentQueue<DateTime>();
        }

        /// <summary>
        /// Executes an async operation while respecting rate limits
        /// </summary>
        public async Task ExecuteAsync(Func<Task> operation, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                await EnforceRateLimit(cancellationToken);
                await operation();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task EnforceRateLimit(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            _accessTimes.Enqueue(now);

            while (_accessTimes.Count > _maxParallel)
            {
                _accessTimes.TryDequeue(out _);
            }

            if (_accessTimes.Count == _maxParallel)
            {
                if (_accessTimes.TryPeek(out var oldestAccess))
                {
                    var timeSinceOldest = (now - oldestAccess).TotalMilliseconds;
                    if (timeSinceOldest < _intervalMs)
                    {
                        await Task.Delay(_intervalMs - (int)timeSinceOldest, cancellationToken);
                    }
                }
            }
        }
    }
}