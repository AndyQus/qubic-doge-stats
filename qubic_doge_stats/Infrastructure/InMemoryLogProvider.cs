using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace qubic_doge_stats.Infrastructure;

public class InMemoryLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Level { get; init; } = "";
    public string Category { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Exception { get; init; }
}

public class InMemoryLogBuffer
{
    private const int MaxEntries = 500;
    private readonly ConcurrentQueue<InMemoryLogEntry> _queue = new();

    public void Add(InMemoryLogEntry entry)
    {
        _queue.Enqueue(entry);
        while (_queue.Count > MaxEntries)
            _queue.TryDequeue(out _);
    }

    public List<InMemoryLogEntry> GetAll() => _queue.ToList();
}

public class InMemoryLoggerProvider(InMemoryLogBuffer buffer) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName, buffer);
    public void Dispose() { }
}

public class InMemoryLogger(string category, InMemoryLogBuffer buffer) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        buffer.Add(new InMemoryLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level     = logLevel.ToString(),
            Category  = category,
            Message   = formatter(state, exception),
            Exception = exception?.ToString(),
        });
    }
}
