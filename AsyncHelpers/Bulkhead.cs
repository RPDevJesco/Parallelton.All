namespace AsyncHelpers
{
    /// <summary>
    /// Implements the Bulkhead pattern to isolate different parts of the system
    /// </summary>
    public class Bulkhead
    {
        private readonly SemaphoreSlim _executionSemaphore;
        private readonly SemaphoreSlim _queueSemaphore;

        public Bulkhead(int maxParallelization, int maxQueuingActions)
        {
            _executionSemaphore = new SemaphoreSlim(maxParallelization);
            _queueSemaphore = new SemaphoreSlim(maxQueuingActions);
        }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            if (!await _queueSemaphore.WaitAsync(TimeSpan.Zero))
            {
                throw new BulkheadRejectedException("Bulkhead queue is full");
            }

            try
            {
                await _executionSemaphore.WaitAsync(cancellationToken);
                try
                {
                    return await operation(cancellationToken);
                }
                finally
                {
                    _executionSemaphore.Release();
                }
            }
            finally
            {
                _queueSemaphore.Release();
            }
        }
    }
}