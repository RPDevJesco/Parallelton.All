using System.Threading.Tasks.Dataflow;

namespace AsyncHelpers
{
    /// <summary>
    /// Provides functionality for processing items in batches
    /// </summary>
    public class BatchProcessor<T>
    {
        private readonly int _batchSize;
        private readonly TimeSpan _maxWaitTime;
        private readonly BufferBlock<T> _buffer;
        private readonly ActionBlock<IList<T>> _processor;
        private readonly CancellationTokenSource _cts;
        private readonly Timer _timer;
        private readonly object _lock = new object();
        private List<T> _currentBatch;

        public BatchProcessor(
            Func<IList<T>, Task> processBatch,
            int batchSize,
            TimeSpan maxWaitTime,
            int maxDegreeOfParallelism = 1)
        {
            _batchSize = batchSize;
            _maxWaitTime = maxWaitTime;
            _currentBatch = new List<T>();
            _cts = new CancellationTokenSource();

            var options = new ExecutionDataflowBlockOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = _cts.Token
            };

            _processor = new ActionBlock<IList<T>>(
                async items => await processBatch(items),
                options);

            _buffer = new BufferBlock<T>();
            _timer = new Timer(ProcessTimeout, null, Timeout.Infinite, Timeout.Infinite);

            StartProcessing();
        }

        public async Task AddItemAsync(T item)
        {
            await _buffer.SendAsync(item);
        }

        private void StartProcessing()
        {
            Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var item = await _buffer.ReceiveAsync(_cts.Token);
                    
                    lock (_lock)
                    {
                        _currentBatch.Add(item);
                        
                        if (_currentBatch.Count >= _batchSize)
                        {
                            ProcessBatch();
                        }
                        else if (_currentBatch.Count == 1)
                        {
                            _timer.Change(_maxWaitTime, Timeout.InfiniteTimeSpan);
                        }
                    }
                }
            }, _cts.Token);
        }

        private void ProcessTimeout(object? state)
        {
            lock (_lock)
            {
                if (_currentBatch.Count > 0)
                {
                    ProcessBatch();
                }
            }
        }

        private void ProcessBatch()
        {
            var batch = _currentBatch;
            _currentBatch = new List<T>();
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _processor.Post(batch);
        }

        public async Task CompleteAsync()
        {
            _cts.Cancel();
            _buffer.Complete();
            await _processor.Completion;
        }
    }
}