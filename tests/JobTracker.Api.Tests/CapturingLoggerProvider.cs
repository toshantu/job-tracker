using Microsoft.Extensions.Logging;

namespace JobTracker.Api.Tests;

// Collects every log line the test host writes, from every category, so a test can prove
// that no key, user text or provider text ever reaches the logs.
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly List<string> _messages = new();

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (_gate)
            {
                return _messages.ToList();
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Add(string message)
    {
        lock (_gate)
        {
            _messages.Add(message);
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly CapturingLoggerProvider _provider;
        private readonly string _category;

        public CapturingLogger(CapturingLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var text = $"[{_category}] {formatter(state, exception)}";

            if (exception is not null)
            {
                text += " | " + exception;
            }

            _provider.Add(text);
        }
    }
}
