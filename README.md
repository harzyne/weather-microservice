# Weather Microservice

Small ASP.NET Core (minimal API) microservice that fetches weather data from external providers, persists readings, exposes endpoints for current/forecast/historical data, supports CSV export and webhook subscriptions, and includes tests.

- Target framework: .NET 9 (net9.0)
- Location: `src/Weather.Api`

Quickstart
----------
Prerequisites
- .NET 9 SDK
- (Optional) An OpenWeatherMap API key if you want live OpenWeatherMap data

Run locally
1. Set any secrets or configuration you need. Example (PowerShell):

   $Env:Jwt__Secret = "your-very-strong-secret"; $Env:OpenWeatherMap__ApiKey = "<your-key>"

   Note: Configuration keys use double-underscore for nesting (e.g. `Jwt:Secret` -> `Jwt__Secret`).

2. Run the API:

   dotnet run --project src/Weather.Api

3. The app will listen on the configured ports. By default the HTTPS redirect port used in launch settings is 7024, and Swagger UI is available at:

   https://localhost:7024/swagger

Development conveniences
- A development-only token endpoint is available when running in Development and when `Auth:DevMode` is true: `GET /dev/token`. This issues an HS256 JWT signed with the configured `Jwt:Secret` and is intended for local testing only.
- Tests run with a test host that forces the environment to `Development`, enables `Auth:DevMode`, replaces the EF Core store with an InMemory database and disables the background poller to make tests deterministic. See `tests/Weather.Tests/CustomWebApplicationFactory.cs`.

Database
- By default the app uses SQLite with connection string `Data Source=weather.db` if no connection string named `Sqlite` is provided.
- For local development you can override `ConnectionStrings:Sqlite` via environment variables or appsettings.
- The app currently calls `EnsureCreated()` at startup; migrations are not wired up yet.

Authentication
- JWT Bearer (HS256) is used. Configure `Jwt:Issuer`, `Jwt:Audience`, and `Jwt:Secret` in appsettings or environment.
- For local dev, the code hashes your `Jwt:Secret` to produce a 256-bit signing key (this is a dev convenience; in production use proper key rotation and KMS/Key Vault).

Tests
- Unit & integration tests live in `tests/Weather.Tests`.
- Run all tests:

  dotnet test

- The test factory overrides configuration to use InMemory EF provider and disables the background worker so tests are deterministic.

Project structure
- src/Weather.Api: API project (Program.cs, services, DbContext, background worker)
- tests/Weather.Tests: xUnit tests with WebApplicationFactory-based integration tests

License
- No license file included. Add a LICENSE if you plan to publish this repository.
