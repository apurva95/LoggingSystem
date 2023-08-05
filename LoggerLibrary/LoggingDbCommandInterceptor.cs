//using Microsoft.EntityFrameworkCore;
//using Microsoft.EntityFrameworkCore.Diagnostics;
//using Microsoft.Extensions.Logging;
//using System.Data.Common;

//namespace LoggerLibrary
//{
//    public class LoggingDbCommandInterceptor : IDbCommandInterceptor
//    {
//        private readonly ILogger _logger;

//        public LoggingDbCommandInterceptor(ILogger logger)
//        {
//            _logger = logger;
//        }

//        public InterceptionResult<DbDataReader> ReaderExecuting(
//            DbCommand command,
//            CommandEventData eventData,
//            InterceptionResult<DbDataReader> result)
//        {
//            _logger.LogInformation(command.CommandText);
//            return result;
//        }

//        // Implement other methods of IDbCommandInterceptor...
//    }

//    public static class LoggingExtensions
//    {
//        public static DbContextOptionsBuilder UseLoggingInterceptor(this DbContextOptionsBuilder optionsBuilder, ILogger logger)
//        {
//            return optionsBuilder.AddInterceptors(new LoggingDbCommandInterceptor(logger));
//        }
//    }
//}
