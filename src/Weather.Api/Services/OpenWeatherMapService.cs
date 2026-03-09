using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Weather.Api.Services
{
    internal class OpenWeatherMapService : IWeatherService
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;

        public OpenWeatherMapService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["OpenWeatherMap:ApiKey"] ?? string.Empty;
        }

        public async Task<CurrentWeatherDto?> GetCurrentAsync(string? city = null, double? lat = null, double? lon = null)
        {
            if (string.IsNullOrEmpty(_apiKey)) return null;

            string url;
            if (!string.IsNullOrEmpty(city))
                url = $"https://api.openweathermap.org/data/2.5/weather?q={Uri.EscapeDataString(city)}&appid={_apiKey}&units=metric";
            else if (lat.HasValue && lon.HasValue)
                url = $"https://api.openweathermap.org/data/2.5/weather?lat={lat}&lon={lon}&appid={_apiKey}&units=metric";
            else
                return null;

            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("main", out var main)) return null;

            double temp = main.GetProperty("temp").GetDouble();
            int humidity = main.GetProperty("humidity").GetInt32();

            string? descr = null;
            if (root.TryGetProperty("weather", out var weather) && weather.ValueKind == JsonValueKind.Array && weather.GetArrayLength() > 0)
            {
                var first = weather[0];
                if (first.TryGetProperty("description", out var descEl))
                    descr = descEl.GetString();
            }

            double? wind = null;
            if (root.TryGetProperty("wind", out var windEl) && windEl.TryGetProperty("speed", out var speedEl) && speedEl.ValueKind == JsonValueKind.Number)
            {
                wind = speedEl.GetDouble();
            }

            string name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? city ?? string.Empty : city ?? string.Empty;
            DateTime timestamp = DateTime.UtcNow;
            if (root.TryGetProperty("dt", out var dtEl) && dtEl.ValueKind == JsonValueKind.Number)
            {
                var unix = dtEl.GetInt64();
                timestamp = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime;
            }

            return new CurrentWeatherDto(name, temp, humidity, descr, wind, timestamp);
        }

        public async Task<IEnumerable<ForecastDto>> GetForecastAsync(string? city = null, double? lat = null, double? lon = null)
        {
            if (string.IsNullOrEmpty(_apiKey)) return Array.Empty<ForecastDto>();

            string url;
            if (!string.IsNullOrEmpty(city))
                url = $"https://api.openweathermap.org/data/2.5/forecast?q={Uri.EscapeDataString(city)}&appid={_apiKey}&units=metric";
            else if (lat.HasValue && lon.HasValue)
                url = $"https://api.openweathermap.org/data/2.5/forecast?lat={lat}&lon={lon}&appid={_apiKey}&units=metric";
            else
                return Array.Empty<ForecastDto>();

            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var results = new List<ForecastDto>();
            if (root.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    DateTime ts = DateTime.UtcNow;
                    if (item.TryGetProperty("dt", out var dtEl) && dtEl.ValueKind == JsonValueKind.Number)
                    {
                        ts = DateTimeOffset.FromUnixTimeSeconds(dtEl.GetInt64()).UtcDateTime;
                    }

                    double temp = item.GetProperty("main").GetProperty("temp").GetDouble();
                    int humidity = item.GetProperty("main").GetProperty("humidity").GetInt32();

                    string? descr = null;
                    if (item.TryGetProperty("weather", out var weather) && weather.ValueKind == JsonValueKind.Array && weather.GetArrayLength() > 0)
                    {
                        var first = weather[0];
                        if (first.TryGetProperty("description", out var descEl))
                            descr = descEl.GetString();
                    }

                    results.Add(new ForecastDto(ts, temp, humidity, descr));
                }
            }

            return results;
        }
    }
}
