using System.Collections.Concurrent;

namespace AsyncHelpers
{
    /// <summary>
    /// Provides deadlock detection capabilities for async operations
    /// </summary>
    public class DeadlockDetector
    {
        private readonly ConcurrentDictionary<int, ResourceAllocation> _threadResources
            = new ConcurrentDictionary<int, ResourceAllocation>();
        
        private readonly ConcurrentDictionary<string, HashSet<int>> _resourceThreads
            = new ConcurrentDictionary<string, HashSet<int>>();

        public async Task<T> MonitorOperation<T>(
            string resourceId,
            Func<Task<T>> operation,
            TimeSpan timeout)
        {
            var threadId = Environment.CurrentManagedThreadId;
            var allocation = new ResourceAllocation(resourceId, DateTime.UtcNow);

            if (DetectDeadlock(threadId, resourceId))
            {
                throw new DeadlockDetectedException($"Potential deadlock detected for resource {resourceId}");
            }

            _threadResources.AddOrUpdate(threadId, allocation, (_, __) => allocation);
            var threads = _resourceThreads.GetOrAdd(resourceId, _ => new HashSet<int>());
            lock (threads)
            {
                threads.Add(threadId);
            }

            try
            {
                using var cts = new CancellationTokenSource(timeout);
                return await operation().WithTimeout(timeout, cts.Token);
            }
            finally
            {
                _threadResources.TryRemove(threadId, out _);
                lock (threads)
                {
                    threads.Remove(threadId);
                }
            }
        }

        private bool DetectDeadlock(int threadId, string requestedResource)
        {
            var visited = new HashSet<int>();
            var stack = new Stack<int>();
            stack.Push(threadId);

            while (stack.Count > 0)
            {
                var currentThread = stack.Pop();
                visited.Add(currentThread);

                if (_threadResources.TryGetValue(currentThread, out var allocation))
                {
                    var threadsHoldingResource = _resourceThreads.GetOrAdd(
                        allocation.ResourceId,
                        _ => new HashSet<int>());

                    lock (threadsHoldingResource)
                    {
                        foreach (var holdingThread in threadsHoldingResource)
                        {
                            if (holdingThread != currentThread &&
                                !visited.Contains(holdingThread))
                            {
                                if (_threadResources.TryGetValue(holdingThread, out var holdingAllocation) &&
                                    holdingAllocation.ResourceId == requestedResource)
                                {
                                    return true;
                                }

                                stack.Push(holdingThread);
                            }
                        }
                    }
                }
            }

            return false;
        }

        private class ResourceAllocation
        {
            public string ResourceId { get; }
            public DateTime Timestamp { get; }

            public ResourceAllocation(string resourceId, DateTime timestamp)
            {
                ResourceId = resourceId;
                Timestamp = timestamp;
            }
        }
    }
}