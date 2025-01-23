namespace ThreadingHelpers
{
    /// <summary>
    /// Provides a simple API for executing tasks periodically
    /// </summary>
    public class PeriodicTask : IDisposable
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Task _task;

        private PeriodicTask(Func<Task> action, TimeSpan interval, CancellationTokenSource cancellation)
        {
            _cancellation = cancellation;
            _task = RunPeriodically(action, interval, cancellation.Token);
        }

        public static PeriodicTask Run(Func<Task> action, TimeSpan interval)
        {
            return new PeriodicTask(action, interval, new CancellationTokenSource());
        }

        private async Task RunPeriodically(Func<Task> action, TimeSpan interval, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await action();
                await Task.Delay(interval, token);
            }
        }

        public void Stop()
        {
            _cancellation.Cancel();
        }

        public async Task StopAndWait()
        {
            _cancellation.Cancel();
            await _task;
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        }
    }
}