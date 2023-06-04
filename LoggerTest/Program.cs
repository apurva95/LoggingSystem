using LoggerLibrary;
using LoggerTest;

var builder = WebApplication.CreateBuilder(args);

// Retrieve configuration values
var configuration = builder.Configuration;
var loggerConfigSection = configuration.GetSection("LoggerConfiguration");
var loggerConfiguration = loggerConfigSection.Get<LoggerConfiguration>();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.CheckConsentNeeded = context => false;
    options.MinimumSameSitePolicy = SameSiteMode.None;
});
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    // Configure session options as needed
    options.IdleTimeout = TimeSpan.FromDays(1);
    options.Cookie.Name = "mySession";
});

builder.Services.AddHttpContextAccessor(); // Add the HttpContextAccessor
builder.Services.AddScoped<ISessionIdProvider, HttpContextSessionIdProvider>();
ISessionIdProvider sessionIdProvider = builder.Services.BuildServiceProvider().GetRequiredService<ISessionIdProvider>();

builder.Logging.AddProvider(new CustomLoggerProvider(loggerConfiguration, sessionIdProvider));

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();
app.UseAuthorization();
app.UseSession();
app.MapControllers();
app.Run();
