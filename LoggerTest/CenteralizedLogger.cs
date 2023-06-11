using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Threading.Tasks;

namespace LoggerTest
{
    // You may need to install the Microsoft.AspNetCore.Http.Abstractions package into your project
    public class CenteralizedLogger
    {
        private readonly RequestDelegate _next;
        private ILogger _logger;

        public CenteralizedLogger(RequestDelegate next, ILogger<CenteralizedLogger> logger)
        {
            _next = next;
            _logger = logger;
        }

        public Task Invoke(HttpContext httpContext)
        {
            try
            {
                _logger.LogInformation(ex.Message);
                return _next(httpContext);
            }
            catch(Exception ex)
            {
                _logger.LogCritical(ex.Message);
                return Task.FromResult(HttpStatusCode.ServiceUnavailable);
            }
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class CentrlizedLoggerExtensions
    {
        public static IApplicationBuilder UseCenteralizedLogger(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CenteralizedLogger>();
        }
    }
}
