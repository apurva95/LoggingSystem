using LoggerLibrary;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
var loggerConfiguration = builder.Configuration.GetSection("LoggerConfiguration").Get<LoggerConfiguration>();
builder.Logging.AddProvider(new CustomLoggerProvider(loggerConfiguration));

var app = builder.Build();
app.UseCenteralizedLogger();

// Configure the HTTP request pipeline.

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
