using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Weather.Api.Data;
using Weather.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace Weather.Api.Background
{
    public record SubscriptionInput(string CallbackUrl, string? Location, double ThresholdTemperature);

    public class WeatherPoller : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<WeatherPoller> _logger;
        private readonly IHttpClientFactory _httpFactory;
        private readonly TimeSpan _interval;

        public WeatherPoller(IServiceScopeFactory scopeFactory, ILogger<WeatherPoller> logger, IHttpClientFactory httpFactory, IConfiguration config)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _httpFactory = httpFactory;
            var minutes = config.GetValue<int?>("Polling:Minutes") ?? 5;
            _interval = TimeSpan.FromMinutes(minutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
                    var open = scope.ServiceProvider.GetRequiredService<OpenWeatherMapService>();
                    var datagov = scope.ServiceProvider.GetRequiredService<DataGovSgService>();

                    // Fetch and persist
                    var cw = await open.GetCurrentAsync("Singapore");
                    if (cw != null)
                    {
                        db.Weather.Add(new PersistedWeather { Provider = "OpenWeatherMap", Location = cw.Location, Timestamp = cw.Timestamp, TemperatureC = cw.TemperatureC, Humidity = cw.Humidity, Description = cw.Description });
                    }

                    var dg = await datagov.GetCurrentAsync();
                    if (dg != null)
                    {
                        db.Weather.Add(new PersistedWeather { Provider = "DataGovSg", Location = dg.Location, Timestamp = dg.Timestamp, TemperatureC = dg.TemperatureC, Humidity = dg.Humidity, Description = dg.Description });
                    }

                    await db.SaveChangesAsync(stoppingToken);

                    // Trigger simple webhooks
                    var subs = await db.Subscriptions.ToListAsync(stoppingToken);
                    foreach (var s in subs)
                    {
                        var latest = await db.Weather.Where(w => w.Location == s.Location).OrderByDescending(w => w.Timestamp).FirstOrDefaultAsync(stoppingToken);
                        if (latest != null && latest.TemperatureC >= s.ThresholdTemperature)
                        {
                            _ = FireWebhookAsync(s.CallbackUrl, new { latest.Location, latest.TemperatureC, latest.Timestamp }, stoppingToken);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during polling");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }

        private async Task FireWebhookAsync(string url, object payload, CancellationToken ct)
        {
            try
            {
                var client = _httpFactory.CreateClient();
                var json = JsonSerializer.Serialize(payload);
                using var resp = await client.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send webhook to {url}", url);
            }
        }
    }
}
