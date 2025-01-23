using System.Threading.Tasks.Dataflow;

namespace AsyncHelpers
{
    /// <summary>
    /// Base class for implementing the Actor pattern
    /// </summary>
    public abstract class Actor : IDisposable
    {
        private readonly BufferBlock<Func<Task>> _mailbox;
        private readonly CancellationTokenSource _cts;
        private readonly Task _processingTask;

        protected Actor()
        {
            _mailbox = new BufferBlock<Func<Task>>();
            _cts = new CancellationTokenSource();
            _processingTask = ProcessMessages();
        }

        protected async Task Tell(Func<Task> message)
        {
            await _mailbox.SendAsync(message);
        }

        private async Task ProcessMessages()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var message = await _mailbox.ReceiveAsync(_cts.Token);
                    await message();
                }
            }
            catch (OperationCanceledException) { }
        }

        public async Task StopAsync()
        {
            _cts.Cancel();
            _mailbox.Complete();
            await _processingTask;
        }

        public void Dispose()
        {
            _cts.Dispose();
        }
    }
}