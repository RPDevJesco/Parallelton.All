namespace AsyncHelpers
{
    public class DeadlockDetectedException : Exception
    {
        public DeadlockDetectedException(string message) : base(message)
        {
        }
    }
}