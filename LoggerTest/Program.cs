using LoggerLibrary;

var builder = WebApplication.CreateBuilder(args);

// Retrieve configuration values
var loggerConfiguration = builder.Configuration.GetSection("LoggerConfiguration").Get<LoggerLibrary.LoggerConfiguration>();
builder.Services.AddControllers();
builder.Logging.AddProvider(new CustomLoggerProvider(loggerConfiguration));

var app = builder.Build();
app.UseCenteralizedLogger();
// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
