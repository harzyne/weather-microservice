using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using Weather.Api.Data;

namespace Weather.Tests
{
    public class CustomWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Force the test host to run in Development so dev-only endpoints/middleware are enabled
            builder.UseEnvironment("Development");

            builder.ConfigureAppConfiguration((context, conf) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "test-issuer",
                    ["Jwt:Audience"] = "test-audience",
                    ["Jwt:Secret"] = "this-is-a-very-long-test-secret-which-is-secure",
                    ["Auth:DevMode"] = "true"
                };
                conf.AddInMemoryCollection(settings);
            });

            builder.ConfigureServices(services =>
            {
                // Remove existing DbContext registrations
                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<WeatherDbContext>));
                if (dbContextDescriptor != null) services.Remove(dbContextDescriptor);

                var weatherDbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(WeatherDbContext));
                if (weatherDbDescriptor != null) services.Remove(weatherDbDescriptor);

                // Register InMemory database for tests
                services.AddDbContext<WeatherDbContext>(options =>
                {
                    options.UseInMemoryDatabase("TestDb");
                });

                // Ensure database is created
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
                db.Database.EnsureCreated();

                // Remove background poller hosted service (if registered) to avoid interference during tests
                var pollerDescriptors = services.Where(d => d.ImplementationType != null && d.ImplementationType.Name == "WeatherPoller").ToList();
                foreach (var pd in pollerDescriptors) services.Remove(pd);
            });
        }
    }
}
