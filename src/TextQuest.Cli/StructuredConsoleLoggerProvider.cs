using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

internal sealed class StructuredConsoleLoggerProvider : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly ConcurrentDictionary<string, StructuredConsoleLogger> _loggers = new(StringComparer.Ordinal);

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, static (name, state) => new StructuredConsoleLogger(name, state), _sync);
    }

    public void Dispose()
    {
    }

    private sealed class StructuredConsoleLogger(string categoryName, object sync) : ILogger
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["level"] = logLevel.ToString(),
                ["category"] = categoryName,
                ["eventId"] = eventId.Id,
                ["eventName"] = string.IsNullOrWhiteSpace(eventId.Name) ? null : eventId.Name,
                ["message"] = formatter(state, exception),
            };

            if (exception is not null)
            {
                payload["exception"] = exception.ToString();
            }

            if (state is IEnumerable<KeyValuePair<string, object?>> properties)
            {
                payload["properties"] = properties
                    .Where(pair => !string.Equals(pair.Key, "{OriginalFormat}", StringComparison.Ordinal) && !string.Equals(pair.Key, "OriginalFormat", StringComparison.Ordinal))
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }

            var line = JsonSerializer.Serialize(payload, SerializerOptions);
            lock (sync)
            {
                Console.Error.WriteLine(line);
            }
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
