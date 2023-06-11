using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LoggerLibrary
{
    public class CustomLoggerProvider : ILoggerProvider
    {
        private readonly LoggerConfiguration _configuration;
        public CustomLoggerProvider(LoggerConfiguration logger)
        {
            _configuration = logger;
        }

        public ILogger CreateLogger(string categoryName)
        {
            //can be switheced via Factory -> Queue, Db, S3
            return new CustomLogger(_configuration);
        }

        public void Dispose()
        {
        }
    }
}
