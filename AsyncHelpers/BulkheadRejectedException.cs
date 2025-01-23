namespace AsyncHelpers
{
    public class BulkheadRejectedException : Exception
    {
        public BulkheadRejectedException(string message) : base(message)
        {
        }
    }
}