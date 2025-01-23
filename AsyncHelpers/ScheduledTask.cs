namespace AsyncHelpers
{
    internal class ScheduledTask : IComparable<ScheduledTask>
    {
        public Guid Id { get; }
        public Func<CancellationToken, Task> Operation { get; }
        public TaskPriority Priority { get; }
        public DateTime ScheduledTime { get; }
        public TimeSpan? Timeout { get; }

        public ScheduledTask(
            Guid id,
            Func<CancellationToken, Task> operation,
            TaskPriority priority,
            DateTime scheduledTime,
            TimeSpan? timeout)
        {
            Id = id;
            Operation = operation;
            Priority = priority;
            ScheduledTime = scheduledTime;
            Timeout = timeout;
        }

        public int CompareTo(ScheduledTask? other)
        {
            if (other == null) return 1;

            var priorityComparison = other.Priority.CompareTo(Priority);
            return priorityComparison != 0
                ? priorityComparison
                : ScheduledTime.CompareTo(other.ScheduledTime);
        }
    }
}