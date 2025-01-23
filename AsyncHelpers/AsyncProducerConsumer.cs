using System.Collections.Concurrent;

namespace AsyncHelpers
{
    /// <summary>
    /// Implements a producer-consumer pattern with async support
    /// </summary>
    public class AsyncProducerConsumer<T> : IDisposable
    {
        private readonly BlockingCollection<T> _queue;
        private readonly int _maxQueueSize;
        private readonly CancellationTokenSource _cts;

        public AsyncProducerConsumer(int maxQueueSize = -1)
        {
            _maxQueueSize = maxQueueSize;
            _queue = maxQueueSize > 0 
                ? new BlockingCollection<T>(maxQueueSize)
                : new BlockingCollection<T>();
            _cts = new CancellationTokenSource();
        }

        public async Task ProduceAsync(T item)
        {
            await Task.Run(() => _queue.Add(item), _cts.Token);
        }

        public async Task<T> ConsumeAsync()
        {
            return await Task.Run(() => _queue.Take(), _cts.Token);
        }

        public void Complete()
        {
            _queue.CompleteAdding();
        }

        public void Cancel()
        {
            _cts.Cancel();
        }

        public void Dispose()
        {
            _cts.Dispose();
            _queue.Dispose();
        }
    }
}