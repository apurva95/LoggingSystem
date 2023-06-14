using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Reflection;

namespace LoggerLibrary
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

        public async Task Invoke(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, ex.Message);
                httpContext.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            }
        }
    }

    // Extension method used to add the middleware to the HTTP request pipeline.
    public static class CentralizedLoggerExtensions
    {
        public static IApplicationBuilder UseCenteralizedLogger(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CenteralizedLogger>();
        }
    }
}
