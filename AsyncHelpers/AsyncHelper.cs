namespace AsyncHelpers
{
    /// <summary>
    /// Provides methods for managing and controlling async operations
    /// </summary>
    public static class AsyncHelper
    {
        #region Parallel Processing

        /// <summary>
        /// Executes multiple tasks with a specified degree of parallelism
        /// </summary>
        public static async Task ParallelForEachAsync<T>(
            IEnumerable<T> items,
            int maxConcurrency,
            Func<T, Task> operation,
            CancellationToken cancellationToken = default)
        {
            using var semaphore = new SemaphoreSlim(maxConcurrency);
            var tasks = new List<Task>();

            foreach (var item in items)
            {
                await semaphore.WaitAsync(cancellationToken);
                
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await operation(item);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken));
            }

            await Task.WhenAll(tasks);
        }

        #endregion

        #region Retry Logic

        /// <summary>
        /// Retries an async operation with exponential backoff
        /// </summary>
        public static async Task<T> RetryWithBackoffAsync<T>(
            Func<Task<T>> operation,
            int maxAttempts = 3,
            int initialDelayMs = 100,
            double backoffFactor = 2.0,
            CancellationToken cancellationToken = default)
        {
            var delay = initialDelayMs;
            Exception? lastException = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    lastException = ex;
                    await Task.Delay(delay, cancellationToken);
                    delay = (int)(delay * backoffFactor);
                }
            }

            throw new AggregateException($"Operation failed after {maxAttempts} attempts", lastException!);
        }

        #endregion

        #region Timeout Extensions

        /// <summary>
        /// Creates a task that completes after a timeout or when the source task completes
        /// </summary>
        public static async Task<T> WithTimeout<T>(
            this Task<T> task,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var timeoutTask = Task.Delay(timeout, timeoutCts.Token);
            var completedTask = await Task.WhenAny(task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException($"Operation timed out after {timeout.TotalSeconds} seconds");
            }

            timeoutCts.Cancel();
            return await task;
        }

        #endregion
    }
}