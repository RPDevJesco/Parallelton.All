using System.Collections.Concurrent;

namespace AsyncHelpers
{
    /// <summary>
    /// Implements a pub/sub event aggregator with async support
    /// </summary>
    public class AsyncEventAggregator
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers 
            = new ConcurrentDictionary<Type, List<Delegate>>();

        public void Subscribe<T>(Func<T, Task> handler)
        {
            var handlers = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());
            lock (handlers)
            {
                handlers.Add(handler);
            }
        }

        public void Unsubscribe<T>(Func<T, Task> handler)
        {
            if (_handlers.TryGetValue(typeof(T), out var handlers))
            {
                lock (handlers)
                {
                    handlers.Remove(handler);
                }
            }
        }

        public async Task PublishAsync<T>(T message)
        {
            if (_handlers.TryGetValue(typeof(T), out var handlers))
            {
                var tasks = new List<Task>();
                
                lock (handlers)
                {
                    foreach (Func<T, Task> handler in handlers)
                    {
                        tasks.Add(handler(message));
                    }
                }

                await Task.WhenAll(tasks);
            }
        }
    }
}