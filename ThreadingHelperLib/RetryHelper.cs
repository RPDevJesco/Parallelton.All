namespace ThreadingHelpers
{
    /// <summary>
    /// Provides methods for running tasks with retry logic
    /// </summary>
    public static class RetryHelper
    {
        /// <summary>
        /// Executes an async operation with retry logic
        /// </summary>
        /// <param name="operation">The operation to execute</param>
        /// <param name="maxAttempts">Maximum number of retry attempts</param>
        /// <param name="delayMs">Delay between retries in milliseconds</param>
        /// <param name="exponentialBackoff">Whether to use exponential backoff for delays</param>
        public static async Task RetryAsync(
            Func<Task> operation,
            int maxAttempts = 3,
            int delayMs = 1000,
            bool exponentialBackoff = true)
        {
            var attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    await operation();
                    return;
                }
                catch (Exception) when (attempts < maxAttempts)
                {
                    var delay = exponentialBackoff ? delayMs * Math.Pow(2, attempts - 1) : delayMs;
                    await Task.Delay((int)delay);
                }
            }
        }

        /// <summary>
        /// Executes an async operation with retry logic and returns a result
        /// </summary>
        public static async Task<T> RetryAsync<T>(
            Func<Task<T>> operation,
            int maxAttempts = 3,
            int delayMs = 1000,
            bool exponentialBackoff = true)
        {
            var attempts = 0;
            while (true)
            {
                try
                {
                    attempts++;
                    return await operation();
                }
                catch (Exception) when (attempts < maxAttempts)
                {
                    var delay = exponentialBackoff ? delayMs * Math.Pow(2, attempts - 1) : delayMs;
                    await Task.Delay((int)delay);
                }
            }
        }
    }
}