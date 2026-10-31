using Microsoft.Extensions.Logging;

namespace PaymentGateway.Api.Tests.Unit.Logging;

internal sealed class TestLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = ((IEnumerable<KeyValuePair<string, object?>>)state!)
            .ToDictionary(property => property.Key, property => property.Value);
        Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties, exception));
    }
}

internal sealed record LogEntry(
    LogLevel Level, string Message, IReadOnlyDictionary<string, object?> Properties, Exception? Exception);