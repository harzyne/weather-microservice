using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using Weather.Api.Services;

namespace Weather.Tests
{
    public class WeatherApiTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;

        public WeatherApiTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
        }

        [Fact]
        public async Task GetWeatherForecast_ShouldReturn200()
        {
            var client = _factory.CreateClient();
            var resp = await client.GetAsync("/weatherforecast");
            resp.IsSuccessStatusCode.Should().BeTrue();
        }

        [Fact]
        public async Task CurrentEndpoint_NoParams_ReturnsNotFoundOrOk()
        {
            var client = _factory.CreateClient();
            var resp = await client.GetAsync("/api/weather/current");
            // Either NotFound or OK depending on provider config
            resp.StatusCode.Should().Match(s => s == System.Net.HttpStatusCode.OK || s == System.Net.HttpStatusCode.NotFound);
        }
    }
}
