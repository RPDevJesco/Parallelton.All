namespace AsyncHelpers
{
    /// <summary>
    /// Implements the Circuit Breaker pattern to prevent cascading failures
    /// </summary>
    public class CircuitBreaker
    {
        private readonly object _lock = new object();
        private readonly int _failureThreshold;
        private readonly TimeSpan _resetTimeout;
        private readonly TimeSpan _halfOpenTimeout;
        private int _failureCount;
        private DateTime _lastFailureTime;
        private CircuitState _state;

        public enum CircuitState
        {
            Closed,      // Normal operation
            Open,        // Failing, reject all requests
            HalfOpen    // Testing if system has recovered
        }

        public CircuitBreaker(
            int failureThreshold = 5,
            TimeSpan? resetTimeout = null,
            TimeSpan? halfOpenTimeout = null)
        {
            _failureThreshold = failureThreshold;
            _resetTimeout = resetTimeout ?? TimeSpan.FromSeconds(60);
            _halfOpenTimeout = halfOpenTimeout ?? TimeSpan.FromSeconds(10);
            _state = CircuitState.Closed;
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
        {
            await CheckCircuitState();

            try
            {
                var result = await operation();
                Success();
                return result;
            }
            catch (Exception ex)
            {
                RecordFailure();
                throw new CircuitBreakerException("Circuit breaker prevented operation", ex);
            }
        }

        private async Task CheckCircuitState()
        {
            lock (_lock)
            {
                if (_state == CircuitState.Closed)
                {
                    return;
                }

                if (_state == CircuitState.Open)
                {
                    if (DateTime.UtcNow - _lastFailureTime > _resetTimeout)
                    {
                        _state = CircuitState.HalfOpen;
                    }
                    else
                    {
                        throw new CircuitBreakerException("Circuit breaker is open");
                    }
                }
            }

            if (_state == CircuitState.HalfOpen)
            {
                await Task.Delay(_halfOpenTimeout);
            }
        }

        private void Success()
        {
            lock (_lock)
            {
                _failureCount = 0;
                _state = CircuitState.Closed;
            }
        }

        private void RecordFailure()
        {
            lock (_lock)
            {
                _failureCount++;
                _lastFailureTime = DateTime.UtcNow;

                if (_failureCount >= _failureThreshold)
                {
                    _state = CircuitState.Open;
                }
            }
        }
    }
}