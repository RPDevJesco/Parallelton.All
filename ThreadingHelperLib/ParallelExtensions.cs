namespace ThreadingHelpers
{
    /// <summary>
    /// Extension methods for parallel processing
    /// </summary>
    public static class ParallelExtensions
    {
        /// <summary>
        /// Creates a parallel flow configuration for a collection of items
        /// </summary>
        public static ParallelFlow<T> ParallelFlow<T>(this IEnumerable<T> items)
        {
            return new ParallelFlow<T>(items);
        }

        /// <summary>
        /// Processes items in parallel with a maximum degree of parallelism
        /// </summary>
        public static IEnumerable<IEnumerable<T>> Buffer<T>(this IEnumerable<T> items, int count)
        {
            var buffer = new List<T>(count);
            foreach (var item in items)
            {
                buffer.Add(item);
                if (buffer.Count == count)
                {
                    yield return buffer.ToList();
                    buffer.Clear();
                }
            }
            if (buffer.Any())
            {
                yield return buffer;
            }
        }
    }
}