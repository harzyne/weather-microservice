using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Weather.Api.Services;

namespace Weather.Tests.Services
{
    public class OpenWeatherMapServiceTests
    {
        [Fact]
        public async Task GetCurrentAsync_WithNoKey_ReturnsNull()
        {
            var services = new ServiceCollection();
            services.AddHttpClient<OpenWeatherMapService>();
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
            services.AddSingleton<IConfiguration>(config);
            var sp = services.BuildServiceProvider();
            var svc = sp.GetRequiredService<OpenWeatherMapService>();

            var res = await svc.GetCurrentAsync("Singapore");
            res.Should().BeNull();
        }
    }
}
