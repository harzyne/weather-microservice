using System.Threading.Tasks;
using System.Collections.Generic;

namespace Weather.Api.Services
{
    public record CurrentWeatherDto(string Location, double TemperatureC, int Humidity, string? Description, double? WindSpeed, System.DateTime Timestamp);
    public record ForecastDto(System.DateTime Timestamp, double TempC, int Humidity, string? Description);

    public interface IWeatherService
    {
        Task<CurrentWeatherDto?> GetCurrentAsync(string? city = null, double? lat = null, double? lon = null);
        Task<IEnumerable<ForecastDto>> GetForecastAsync(string? city = null, double? lat = null, double? lon = null);
    }
}
