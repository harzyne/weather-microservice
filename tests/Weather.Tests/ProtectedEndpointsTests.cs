using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Weather.Tests
{
    public class ProtectedEndpointsTests : IClassFixture<CustomWebApplicationFactory>
    {
        private readonly CustomWebApplicationFactory _factory;

        public ProtectedEndpointsTests(CustomWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private async Task<string> GetDevTokenAsync()
        {
            var client = _factory.CreateClient();
            var resp = await client.GetAsync("/dev/token");
            resp.EnsureSuccessStatusCode();
            var obj = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            if (obj.TryGetProperty("token", out var tokenEl))
                return tokenEl.GetString() ?? string.Empty;
            return string.Empty;
        }

        [Fact]
        public async Task ProtectedEndpoints_WithValidToken_ShouldAllow()
        {
            var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
            {
                BaseAddress = new System.Uri("https://localhost")
            });

            // Obtain token from the same test server/client instance
            var tokenResp = await client.GetAsync("/dev/token");
            tokenResp.EnsureSuccessStatusCode();
            var obj = await tokenResp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            string token = string.Empty;
            if (obj.TryGetProperty("token", out var tokenEl)) token = tokenEl.GetString() ?? string.Empty;
            token.Should().NotBeNullOrEmpty();

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // POST subscription (attach Authorization header on the request to ensure it's sent)
            var sub = new { CallbackUrl = "https://example.test/webhook", Location = "Singapore", ThresholdTemperature = 30.0 };
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/subscriptions")
            {
                Content = JsonContent.Create(sub)
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var postResp = await client.SendAsync(req);
            if (postResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                var body = await postResp.Content.ReadAsStringAsync();
                // attach body to assertion message
                postResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, $"Response unauthorized: {body}");
            }
            else
            {
                postResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Created);
            }

            // Export CSV (may return NoContent if no data yet)
            var csvReq = new HttpRequestMessage(HttpMethod.Get, "/api/export/csv?location=Singapore");
            csvReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var csvResp = await client.SendAsync(csvReq);
            csvResp.StatusCode.Should().Match(s => s == System.Net.HttpStatusCode.OK || s == System.Net.HttpStatusCode.NoContent);
        }

        [Fact]
        public async Task ProtectedEndpoints_MissingToken_ShouldReturn401()
        {
            var client = _factory.CreateClient();

            var sub = new { CallbackUrl = "https://example.test/webhook", Location = "Singapore", ThresholdTemperature = 30.0 };
            var postResp = await client.PostAsJsonAsync("/api/subscriptions", sub);
            postResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);

            var csvResp = await client.GetAsync("/api/export/csv?location=Singapore");
            csvResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task ProtectedEndpoints_InvalidToken_ShouldReturn401()
        {
            var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.token.here");

            var sub = new { CallbackUrl = "https://example.test/webhook", Location = "Singapore", ThresholdTemperature = 30.0 };
            var postResp = await client.PostAsJsonAsync("/api/subscriptions", sub);
            postResp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
        }
    }
}
