using System.Collections.Concurrent;

namespace AsyncHelpers
{
    /// <summary>
    /// Provides custom scheduling capabilities for async operations
    /// </summary>
    public class CustomTaskScheduler
    {
        private readonly ConcurrentPriorityQueue<ScheduledTask> _taskQueue;
        private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _taskCancellationSources;
        private readonly Timer _schedulerTimer;
        private readonly TaskCompletionSource<bool> _shutdownTcs;
        private readonly int _maxConcurrentTasks;
        private int _currentTasks;

        public CustomTaskScheduler(int maxConcurrentTasks = -1)
        {
            _taskQueue = new ConcurrentPriorityQueue<ScheduledTask>();
            _taskCancellationSources = new ConcurrentDictionary<Guid, CancellationTokenSource>();
            _schedulerTimer = new Timer(ProcessScheduledTasks, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(100));
            _shutdownTcs = new TaskCompletionSource<bool>();
            _maxConcurrentTasks = maxConcurrentTasks > 0 ? maxConcurrentTasks : int.MaxValue;
            _currentTasks = 0;
        }

        public Guid ScheduleTask(
            Func<CancellationToken, Task> task,
            TaskPriority priority = TaskPriority.Normal,
            DateTime? scheduledTime = null,
            TimeSpan? timeout = null)
        {
            var taskId = Guid.NewGuid();
            var cts = new CancellationTokenSource();
            _taskCancellationSources[taskId] = cts;

            var scheduledTask = new ScheduledTask(
                taskId,
                task,
                priority,
                scheduledTime ?? DateTime.UtcNow,
                timeout);

            _taskQueue.Enqueue(scheduledTask);
            return taskId;
        }

        public bool CancelTask(Guid taskId)
        {
            if (_taskCancellationSources.TryGetValue(taskId, out var cts))
            {
                cts.Cancel();
                return true;
            }
            return false;
        }

        private async void ProcessScheduledTasks(object? state)
        {
            if (_shutdownTcs.Task.IsCompleted)
            {
                return;
            }

            while (_taskQueue.TryPeek(out var nextTask))
            {
                if (nextTask.ScheduledTime > DateTime.UtcNow || _currentTasks >= _maxConcurrentTasks)
                {
                    break;
                }

                if (_taskQueue.TryDequeue(out var task))
                {
                    Interlocked.Increment(ref _currentTasks);
                    _ = ExecuteTaskAsync(task);
                }
            }
        }

        private async Task ExecuteTaskAsync(ScheduledTask task)
        {
            try
            {
                if (_taskCancellationSources.TryGetValue(task.Id, out var cts))
                {
                    var timeoutCts = task.Timeout.HasValue
                        ? CancellationTokenSource.CreateLinkedTokenSource(
                            cts.Token,
                            new CancellationTokenSource(task.Timeout.Value).Token)
                        : cts;

                    await task.Operation(timeoutCts.Token);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Log or handle the exception
            }
            finally
            {
                Interlocked.Decrement(ref _currentTasks);
                _taskCancellationSources.TryRemove(task.Id, out _);
            }
        }

        public async Task ShutdownAsync()
        {
            _schedulerTimer.Dispose();
            foreach (var cts in _taskCancellationSources.Values)
            {
                cts.Cancel();
            }
            _shutdownTcs.SetResult(true);
        }
    }
}