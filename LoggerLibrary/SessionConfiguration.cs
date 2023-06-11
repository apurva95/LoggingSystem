using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using LoggerTest;
using Microsoft.AspNetCore.Builder;

namespace LoggerLibrary
{
    public static class SessionConfiguration
    {
        public static ISessionIdProvider Configure(IServiceCollection services)
        {
            services.Configure<CookiePolicyOptions>(options =>
            {
                options.CheckConsentNeeded = context => false;
                options.MinimumSameSitePolicy = SameSiteMode.None;
            });

            services.AddDistributedMemoryCache();
            services.AddSession(options =>
            {
                // Configure session options as needed
                options.IdleTimeout = TimeSpan.FromDays(1);
                options.Cookie.Name = "mySession";
            });

            services.AddHttpContextAccessor(); // Add the HttpContextAccessor
            services.AddScoped<ISessionIdProvider, HttpContextSessionIdProvider>();
            return services.BuildServiceProvider().GetRequiredService<ISessionIdProvider>();
        }
    }

}
