using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LoggerLibrary
{
    public class CustomLoggerProvider : ILoggerProvider
    {
        private readonly LoggerConfiguration _configuration;
        private readonly ISessionIdProvider _sessionIdProvider;
        public CustomLoggerProvider(LoggerConfiguration logger, ISessionIdProvider sessionIdProvider)
        {
            _configuration = logger;
            _sessionIdProvider = sessionIdProvider;
        }

        public ILogger CreateLogger(string categoryName)
        {
            //can be switheced via Factory -> Queue, Db, S3
            return new CustomLogger(_configuration, _sessionIdProvider);
        }

        public void Dispose()
        {
        }
    }
}
