using System.Collections.Concurrent;

namespace ThreadingHelpers
{
    /// <summary>
    /// Fluent configuration for parallel operations
    /// </summary>
    public class ParallelFlow<T>
    {
        private readonly IEnumerable<T> _items;
        private int _maxParallel = Environment.ProcessorCount;
        private int _retryCount = 0;
        private int _retryDelay = 1000;
        private bool _useExponentialBackoff = true;
        private AdaptiveThrottler _adaptiveThrottler;
        private Action<Exception> _onError;
        private Action<T, Exception> _onItemError;
        private Action<T> _onItemSuccess;
        private int _rateLimit = 0;
        private int _rateLimitInterval = 1000;

        public ParallelFlow(IEnumerable<T> items)
        {
            _items = items ?? throw new ArgumentNullException(nameof(items));
        }
        
        /// <summary>
        /// Sets the adaptive throttler for this flow
        /// </summary>
        internal ParallelFlow<T> SetAdaptiveThrottler(AdaptiveThrottler throttler)
        {
            _adaptiveThrottler = throttler ?? throw new ArgumentNullException(nameof(throttler));
            return this;
        }

        /// <summary>
        /// Sets the maximum number of parallel operations
        /// </summary>
        public ParallelFlow<T> WithMaxParallel(int max)
        {
            if (max <= 0) throw new ArgumentException("Max parallel count must be greater than 0", nameof(max));
            _maxParallel = max;
            return this;
        }

        /// <summary>
        /// Configures retry behavior
        /// </summary>
        public ParallelFlow<T> WithRetry(int count, int delayMs = 1000, bool exponentialBackoff = true)
        {
            if (count < 0) throw new ArgumentException("Retry count must be non-negative", nameof(count));
            if (delayMs < 0) throw new ArgumentException("Delay must be non-negative", nameof(delayMs));
            
            _retryCount = count;
            _retryDelay = delayMs;
            _useExponentialBackoff = exponentialBackoff;
            return this;
        }

        /// <summary>
        /// Sets rate limiting for operations
        /// </summary>
        public ParallelFlow<T> WithRateLimit(int maxOperations, int intervalMs = 1000)
        {
            if (maxOperations <= 0) throw new ArgumentException("Max operations must be greater than 0", nameof(maxOperations));
            if (intervalMs <= 0) throw new ArgumentException("Interval must be greater than 0", nameof(intervalMs));
            
            _rateLimit = maxOperations;
            _rateLimitInterval = intervalMs;
            return this;
        }

        /// <summary>
        /// Configures error handling for individual items
        /// </summary>
        public ParallelFlow<T> OnItemError(Action<T, Exception> handler)
        {
            _onItemError = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <summary>
        /// Configures success handling for individual items
        /// </summary>
        public ParallelFlow<T> OnItemSuccess(Action<T> handler)
        {
            _onItemSuccess = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <summary>
        /// Configures global error handling
        /// </summary>
        public ParallelFlow<T> OnError(Action<Exception> handler)
        {
            _onError = handler ?? throw new ArgumentNullException(nameof(handler));
            return this;
        }

        /// <summary>
        /// Executes the parallel operation with the configured settings
        /// </summary>
        public async Task ForEachAsync(Func<T, Task> operation, CancellationToken cancellationToken = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));

            using var semaphore = new SemaphoreSlim(_maxParallel);
            var rateLimiter = _rateLimit > 0 ? new RateLimiter(_rateLimit, _rateLimitInterval) : null;
            var errors = new ConcurrentBag<Exception>();
            var tasks = new List<Task>();

            foreach (var item in _items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await semaphore.WaitAsync(cancellationToken);

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        if (rateLimiter != null)
                        {
                            await rateLimiter.ExecuteAsync(async () => 
                                await ProcessWithRetry(item, operation, cancellationToken));
                        }
                        else
                        {
                            await ProcessWithRetry(item, operation, cancellationToken);
                        }

                        _onItemSuccess?.Invoke(item);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        _onItemError?.Invoke(item, ex);
                        errors.Add(ex);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);

            if (errors.Any() && _onError != null)
            {
                _onError(new AggregateException(errors));
            }
        }

        private async Task ProcessWithRetry(T item, Func<T, Task> operation, CancellationToken cancellationToken)
        {
            if (_retryCount == 0)
            {
                await operation(item);
                return;
            }

            var attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    await operation(item);
                    return;
                }
                catch (Exception) when (attempts <= _retryCount && !cancellationToken.IsCancellationRequested)
                {
                    if (attempts == _retryCount) throw;
                    
                    var delay = _useExponentialBackoff 
                        ? _retryDelay * Math.Pow(2, attempts - 1) 
                        : _retryDelay;
                        
                    await Task.Delay((int)delay, cancellationToken);
                }
            }
        }
    }
}