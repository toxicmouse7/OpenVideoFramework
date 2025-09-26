using Microsoft.Extensions.Logging;

namespace OpenVideoFramework.Pipelines;

public class PipelineContext
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _pipelineName;

    public PipelineContext(string pipelineName, ILoggerFactory loggerFactory)
    {
        _pipelineName = pipelineName;
        _loggerFactory = loggerFactory;
    }

    public ILogger<T> GetLogger<T>()
    {
        var logger = _loggerFactory.CreateLogger($"{typeof(T).FullName} - {_pipelineName}");
        return new Logger<T>(logger);
    }

    private class Logger<T> : ILogger<T>
    {
        private readonly ILogger _logger;

        public Logger(ILogger logger)
        {
            _logger = logger;
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _logger.Log(logLevel, eventId, state, exception, formatter);
        }

        public bool IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _logger.BeginScope(state);
    }
}