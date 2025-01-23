using System.Collections.Concurrent;

namespace ThreadingHelpers
{
    /// <summary>
    /// Provides a thread-safe worker pool for processing items in parallel
    /// </summary>
    /// <typeparam name="T">The type of items to process</typeparam>
    public class WorkerPool<T>
    {
        private readonly ConcurrentQueue<T> _workQueue;
        private readonly int _maxWorkers;
        private readonly List<Task> _workers;
        private readonly CancellationTokenSource _cancellationSource;
        private readonly Func<T, CancellationToken, Task> _workProcessor;

        public WorkerPool(int maxWorkers, Func<T, CancellationToken, Task> workProcessor)
        {
            _maxWorkers = maxWorkers;
            _workProcessor = workProcessor;
            _workQueue = new ConcurrentQueue<T>();
            _workers = new List<Task>();
            _cancellationSource = new CancellationTokenSource();
        }

        /// <summary>
        /// Adds an item to be processed by the worker pool
        /// </summary>
        public void EnqueueWork(T item)
        {
            _workQueue.Enqueue(item);
            EnsureWorkers();
        }

        /// <summary>
        /// Adds multiple items to be processed by the worker pool
        /// </summary>
        public void EnqueueWork(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                _workQueue.Enqueue(item);
            }
            EnsureWorkers();
        }

        private void EnsureWorkers()
        {
            lock (_workers)
            {
                while (_workers.Count < _maxWorkers && !_workQueue.IsEmpty)
                {
                    _workers.Add(Task.Run(ProcessWork));
                }
            }
        }

        private async Task ProcessWork()
        {
            while (!_cancellationSource.Token.IsCancellationRequested)
            {
                if (_workQueue.TryDequeue(out T item))
                {
                    await _workProcessor(item, _cancellationSource.Token);
                }
                else
                {
                    break;
                }
            }

            lock (_workers)
            {
                _workers.RemoveAll(w => w.Id == Task.CurrentId);
            }
        }

        /// <summary>
        /// Waits for all current work to complete
        /// </summary>
        public async Task WaitForCompletion()
        {
            while (!_workQueue.IsEmpty || _workers.Count > 0)
            {
                await Task.Delay(100);
            }
        }

        /// <summary>
        /// Stops all workers and cancels pending work
        /// </summary>
        public void Stop()
        {
            _cancellationSource.Cancel();
        }
    }
}