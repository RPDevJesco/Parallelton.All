using System.Collections.Concurrent;

namespace AsyncHelpers
{
    internal class ConcurrentPriorityQueue<T> where T : IComparable<T>
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();
        private readonly object _lock = new object();

        public void Enqueue(T item)
        {
            lock (_lock)
            {
                var tempList = new List<T>();
                while (_queue.TryDequeue(out var existing))
                {
                    tempList.Add(existing);
                }

                tempList.Add(item);
                tempList.Sort();

                foreach (var sortedItem in tempList)
                {
                    _queue.Enqueue(sortedItem);
                }
            }
        }

        public bool TryDequeue(out T? result)
        {
            return _queue.TryDequeue(out result);
        }

        public bool TryPeek(out T? result)
        {
            return _queue.TryPeek(out result);
        }
    }
}