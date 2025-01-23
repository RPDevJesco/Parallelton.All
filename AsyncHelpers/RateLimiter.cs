namespace AsyncHelpers
{
    /// <summary>
    /// Provides rate limiting functionality for async operations
    /// </summary>
    public class RateLimiter
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly Queue<DateTime> _requestTimestamps;
        private readonly int _maxRequests;
        private readonly TimeSpan _interval;
        private readonly object _lock = new object();

        public RateLimiter(int maxRequests, TimeSpan interval)
        {
            _maxRequests = maxRequests;
            _interval = interval;
            _semaphore = new SemaphoreSlim(maxRequests);
            _requestTimestamps = new Queue<DateTime>();
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
        {
            await _semaphore.WaitAsync();

            try
            {
                lock (_lock)
                {
                    var now = DateTime.UtcNow;
                    while (_requestTimestamps.Count > 0 && now - _requestTimestamps.Peek() > _interval)
                    {
                        _requestTimestamps.Dequeue();
                    }

                    _requestTimestamps.Enqueue(now);
                }

                return await operation();
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}