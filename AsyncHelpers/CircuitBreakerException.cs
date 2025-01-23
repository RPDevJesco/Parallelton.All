namespace AsyncHelpers
{
    public class CircuitBreakerException : Exception
    {
        public CircuitBreakerException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}