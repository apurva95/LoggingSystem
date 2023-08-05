using LoggerLibrary;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;

namespace LoggerTest.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class WeatherForecastController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };

        private readonly ILogger _logger;

        public WeatherForecastController(ILogger<WeatherForecastController> logger)
        {
            _logger = logger;
        }

        [HttpGet]
        public IEnumerable<WeatherForecast> Get()
        {
            //int a = 0;
            //int b = 10 / a;
            try
            {
                _logger.LogInformation("Test");
                _logger.LogError("Test Error");
                Console.WriteLine("Test");
                return Enumerable.Range(1, 5).Select(index => new WeatherForecast
                {
                    Date = DateTime.Now.AddDays(index),
                    TemperatureC = Random.Shared.Next(-20, 55),
                    Summary = Summaries[Random.Shared.Next(Summaries.Length)]
                })
                .ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex.Message);
                return new List<WeatherForecast>();
            }
        }
    }
}
