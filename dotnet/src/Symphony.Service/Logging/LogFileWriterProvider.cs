using Microsoft.Extensions.Logging;

namespace Symphony.Service.Logging;

public sealed class LogFileWriterProvider : ILoggerProvider
{
    private readonly string _logFile;
    private readonly object _gate = new();

    public LogFileWriterProvider(string logsRoot)
    {
        Directory.CreateDirectory(logsRoot);
        _logFile = Path.Combine(logsRoot, $"symphony-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _logFile, _gate);

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _logFile;
        private readonly object _gate;

        public FileLogger(string categoryName, string logFile, object gate)
        {
            _categoryName = categoryName;
            _logFile = logFile;
            _gate = gate;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel < LogLevel.Information)
            {
                return false;
            }

            if (IsNoisyFrameworkCategory(_categoryName) && logLevel < LogLevel.Warning)
            {
                return false;
            }

            return true;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var line = $"{DateTimeOffset.UtcNow:O} [{logLevel}] {_categoryName} event_id={eventId.Id} {formatter(state, exception)}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (_gate)
            {
                File.AppendAllText(_logFile, line + Environment.NewLine);
            }
        }

        private static bool IsNoisyFrameworkCategory(string categoryName)
        {
            return categoryName.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)
                || categoryName.StartsWith("Microsoft.Hosting.", StringComparison.Ordinal)
                || categoryName.StartsWith("System.Net.Http.HttpClient.", StringComparison.Ordinal);
        }
    }
}

