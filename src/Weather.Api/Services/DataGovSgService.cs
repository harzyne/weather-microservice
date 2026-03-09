using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Weather.Api.Services
{
    public class DataGovSgService : IWeatherService
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;

        public DataGovSgService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _baseUrl = config["DataGovSg:BaseUrl"] ?? "https://api.data.gov.sg/v1";
        }

        public async Task<CurrentWeatherDto?> GetCurrentAsync(string? city = null, double? lat = null, double? lon = null)
        {
            // Use PSI / air temperature endpoints for Singapore. If city provided and not Singapore, return null.
            // For demo, we'll hit the "environment/psi" endpoint for the latest reading and map to DTO.
            var url = $"{_baseUrl}/environment/psi";
            var json = await _http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // data -> items[0] -> readings -> psi_twenty_four_hourly (region) (pick "national")
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
                return null;

            var first = items[0];
            var timestamp = DateTime.UtcNow;
            if (first.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(tsEl.GetString(), out var parsed)) timestamp = parsed.ToUniversalTime();
            }

            int psi = 0;
            string desc = "";
            if (first.TryGetProperty("readings", out var readings) && readings.TryGetProperty("psi_twenty_four_hourly", out var psiEl))
            {
                if (psiEl.TryGetProperty("national", out var nat)) psi = nat.GetInt32();
                desc = $"PSI: {psi}";
            }

            return new CurrentWeatherDto("Singapore", 0, 0, desc, null, timestamp);
        }

        public async Task<IEnumerable<ForecastDto>> GetForecastAsync(string? city = null, double? lat = null, double? lon = null)
        {
            // Data.gov.sg doesn't provide forecast like OpenWeatherMap — return empty
            return System.Array.Empty<ForecastDto>();
        }
    }
}
