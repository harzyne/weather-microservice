using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using System.Linq;
using Weather.Api.Data;
using Weather.Api.Services;
using Microsoft.Extensions.Hosting;
using Weather.Api.Background;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
// Ensure Swagger services are registered (explicitly) for UI and JSON
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add DbContext (SQLite)
var connectionString = builder.Configuration.GetConnectionString("Sqlite") ?? "Data Source=weather.db";
builder.Services.AddDbContext<WeatherDbContext>(options => options.UseSqlite(connectionString));

// HttpClient with Polly resiliency for OpenWeatherMap
builder.Services.AddHttpClient<OpenWeatherMapService>()
    .AddTransientHttpErrorPolicy(p => p.WaitAndRetryAsync(3, _ => TimeSpan.FromSeconds(2)));
// Register as IWeatherService for default provider OpenWeatherMap
builder.Services.AddScoped<IWeatherService, OpenWeatherMapService>();

// Data.gov.sg client
builder.Services.AddHttpClient<DataGovSgService>();

// Register DataGovSgService implementation and ensure OpenWeatherMapService is registered as IWeatherService named default earlier
builder.Services.AddScoped<DataGovSgService>();
// Register background worker
builder.Services.AddHostedService<WeatherPoller>();

// Read JWT settings
var jwtSection = builder.Configuration.GetSection("Jwt");
var issuer = jwtSection["Issuer"] ?? "local-issuer";
var audience = jwtSection["Audience"] ?? "local-audience";
var secret = jwtSection["Secret"] ?? "dev-secret-change-me";
var devMode = builder.Configuration.GetValue<bool?>("Auth:DevMode") ?? true;

var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = issuer,
        ValidateAudience = true,
        ValidAudience = audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = key,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization();

builder.Services.AddHttpsRedirection(options =>
{
    options.HttpsPort = 7024; // match the HTTPS port in Properties/launchSettings.json
});

var app = builder.Build();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WeatherDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
app.MapOpenApi();

app.UseHttpsRedirection();

// Enable the traditional Swagger middleware and UI as a fallback
app.UseSwagger();
app.UseSwaggerUI();

// Dev-only token endpoint
if (app.Environment.IsDevelopment() && devMode)
{
    app.MapGet("/dev/token", () =>
    {
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, "dev-user"), new Claim("role", "tester") };
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(issuer, audience, claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: creds);
        return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
    });
}

// Ensure authentication/authorization middleware are in the pipeline
app.UseAuthentication();
app.UseAuthorization();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

// Endpoints (with improved error handling and logging)
app.MapGet("/api/weather/current", async (IWeatherService svc, string? city, double? lat, double? lon, ILogger<Program> logger) =>
{
    try
    {
        var data = await svc.GetCurrentAsync(city, lat, lon);
        if (data == null)
        {
            logger.LogInformation("Current weather not found for city={City} lat={Lat} lon={Lon}", city, lat, lon);
            return Results.NotFound();
        }
        return Results.Ok(data);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error fetching current weather for city={City} lat={Lat} lon={Lon}", city, lat, lon);
        return Results.Problem("Failed to fetch current weather", statusCode: 500);
    }
});

app.MapGet("/api/weather/forecast", async (IWeatherService svc, string? city, double? lat, double? lon, ILogger<Program> logger) =>
{
    try
    {
        var data = await svc.GetForecastAsync(city, lat, lon);
        return Results.Ok(data);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error fetching forecast for city={City} lat={Lat} lon={Lon}", city, lat, lon);
        return Results.Problem("Failed to fetch forecast", statusCode: 500);
    }
});

app.MapGet("/api/weather/historical", async (WeatherDbContext db, string? location, DateTime? from, DateTime? to, ILogger<Program> logger) =>
{
    try
    {
        var q = db.Weather.AsQueryable();
        if (!string.IsNullOrEmpty(location)) q = q.Where(w => w.Location == location);
        if (from.HasValue) q = q.Where(w => w.Timestamp >= from.Value);
        if (to.HasValue) q = q.Where(w => w.Timestamp <= to.Value);
        var list = await q.OrderByDescending(w => w.Timestamp).Take(500).ToListAsync();
        return Results.Ok(list);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error querying historical weather for location={Location} from={From} to={To}", location, from, to);
        return Results.Problem("Failed to query historical data", statusCode: 500);
    }
});

// CSV export - protected by x-api-key
app.MapGet("/api/export/csv", [Microsoft.AspNetCore.Authorization.Authorize] async (WeatherDbContext db, string? location, DateTime? from, DateTime? to, ILogger<Program> logger) =>
{
    try
    {
        var q = db.Weather.AsQueryable();
        if (!string.IsNullOrEmpty(location)) q = q.Where(w => w.Location == location);
        if (from.HasValue) q = q.Where(w => w.Timestamp >= from.Value);
        if (to.HasValue) q = q.Where(w => w.Timestamp <= to.Value);
        var list = await q.OrderByDescending(w => w.Timestamp).ToListAsync();

        if (list.Count == 0)
        {
            logger.LogInformation("CSV export requested but no data found for location={Location} from={From} to={To}", location, from, to);
            return Results.NoContent();
        }

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Id,Provider,Location,Timestamp,TemperatureC,Humidity,Description");
        foreach (var r in list)
        {
            // escape quotes in description/location
            var loc = r.Location?.Replace("\"", "\"\"") ?? string.Empty;
            var desc = r.Description?.Replace("\"", "\"\"") ?? string.Empty;
            csv.AppendLine($"{r.Id},{r.Provider},\"{loc}\",{r.Timestamp:o},{r.TemperatureC},{r.Humidity},\"{desc}\"");
        }

        logger.LogInformation("CSV export generated for {Count} records", list.Count);
        return Results.File(System.Text.Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "weather.csv");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error exporting CSV for location={Location} from={From} to={To}", location, from, to);
        return Results.Problem("Failed to export CSV", statusCode: 500);
    }
});

// Subscription endpoints with validation and logging
app.MapPost("/api/subscriptions", [Microsoft.AspNetCore.Authorization.Authorize] async (WeatherDbContext db, SubscriptionInput input, ILogger<Program> logger) =>
{
    try
    {
        if (string.IsNullOrWhiteSpace(input.CallbackUrl) || !Uri.IsWellFormedUriString(input.CallbackUrl, UriKind.Absolute))
        {
            logger.LogWarning("Invalid subscription callback URL: {Url}", input.CallbackUrl);
            return Results.BadRequest("Invalid callback URL");
        }

        var sub = new Subscription { CallbackUrl = input.CallbackUrl, Location = input.Location ?? string.Empty, ThresholdTemperature = input.ThresholdTemperature };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        logger.LogInformation("Created subscription {Id} for {Location}", sub.Id, sub.Location);
        return Results.Created($"/api/subscriptions/{sub.Id}", sub);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error creating subscription");
        return Results.Problem("Failed to create subscription", statusCode: 500);
    }
});

app.MapGet("/api/subscriptions", async (WeatherDbContext db, ILogger<Program> logger) =>
{
    try
    {
        var list = await db.Subscriptions.ToListAsync();
        return Results.Ok(list);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error listing subscriptions");
        return Results.Problem("Failed to list subscriptions", statusCode: 500);
    }
});

app.MapDelete("/api/subscriptions/{id}", [Microsoft.AspNetCore.Authorization.Authorize] async (WeatherDbContext db, int id, ILogger<Program> logger) =>
{
    try
    {
        var s = await db.Subscriptions.FindAsync(id);
        if (s == null) return Results.NotFound();
        db.Subscriptions.Remove(s);
        await db.SaveChangesAsync();
        logger.LogInformation("Deleted subscription {Id}", id);
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error deleting subscription {Id}", id);
        return Results.Problem("Failed to delete subscription", statusCode: 500);
    }
});

// Preserve existing /weatherforecast example (unchanged)
app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public partial class Program { }
