# Parallelton.All

High-performance threading and async utilities for .NET applications.

## Libraries

### AsyncHelpers
Async patterns and utilities:
- Actor pattern implementation
- Async event aggregation
- Batch processing
- Bulkhead isolation
- Circuit breaker
- Deadlock detection
- Rate limiting

### ThreadingHelpers
Thread management and system resource utilities:
- Adaptive throttling
- Parallel processing extensions
- Periodic tasks
- System metrics monitoring
- Worker pool management

## Quick Start

```csharp
// Install package
dotnet add package Parallelton.All

// Import namespaces
using AsyncHelpers;
using ThreadingHelpers;

// Example: Process items with retry and rate limiting
await items.ParallelFlow()
    .WithMaxParallel(Environment.ProcessorCount)
    .WithRateLimit(maxOperations: 50, intervalMs: 1000)
    .WithRetry(count: 3)
    .ForEachAsync(async item => {
        await ProcessItem(item);
    });
```

## Features

### Resilience Patterns
- Circuit breaker for failure protection
- Bulkhead isolation
- Exponential backoff retry
- Rate limiting
- Deadlock detection

### Performance Optimization
- Adaptive throttling based on system metrics
- Batch processing with custom timing
- Parallel processing with configurable concurrency
- Priority-based task scheduling

### Monitoring & Control
- Real-time metrics tracking
- System resource monitoring
- Performance anomaly detection
- Throughput and latency measurement
- APDEX scoring

## Usage Examples

### Batch Processing with Circuit Breaker
```csharp
var batchProcessor = new BatchProcessor<LogEntry>(
    processBatch: async entries => {
        await circuitBreaker.ExecuteAsync(async () => {
            await ProcessEntries(entries);
            return true;
        });
    },
    batchSize: 100,
    maxWaitTime: TimeSpan.FromSeconds(5)
);
```

### Adaptive Throttling
```csharp
var throttler = new AdaptiveThrottler(
    targetCpuPercent: 70,
    targetMemoryPercent: 80
);

await throttler.ExecuteAsync(async () => {
    await PerformOperation();
});
```

### Event Aggregation
```csharp
var eventAggregator = new AsyncEventAggregator();

eventAggregator.Subscribe<MetricEvent>(async metric => {
    await ProcessMetric(metric);
});

await eventAggregator.PublishAsync(new MetricEvent { ... });
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Commit changes
4. Push to branch
5. Create Pull Request

## License

MIT License - see LICENSE file for details