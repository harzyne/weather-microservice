using Microsoft.EntityFrameworkCore;
using Weather.Api.Services;

namespace Weather.Api.Data
{
    public class WeatherDbContext : DbContext
    {
        public WeatherDbContext(DbContextOptions<WeatherDbContext> options) : base(options) { }

        public DbSet<PersistedWeather> Weather { get; set; } = null!;
        public DbSet<Subscription> Subscriptions { get; set; } = null!;
    }

    public class PersistedWeather
    {
        public int Id { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public double TemperatureC { get; set; }
        public int Humidity { get; set; }
        public string? Description { get; set; }
    }

    public class Subscription
    {
        public int Id { get; set; }
        public string CallbackUrl { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public double ThresholdTemperature { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
