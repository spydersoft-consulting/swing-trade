using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Spydersoft.Platform.Hosting.StartupExtensions;
using Spydersoft.Platform.Hosting.Telemetry;
using Spydersoft.SwingTrade.Api;
using Spydersoft.SwingTrade.Api.Refresh;
using Spydersoft.SwingTrade.Api.Security;
using Spydersoft.SwingTrade.Api.Services;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.MarketData;

var builder = WebApplication.CreateBuilder(args);

// The OpenAPI document tool (dotnet build -p:GenerateOpenApi=true) builds the host without running it. Give it config that
// needs no database or telemetry collector. Never applies to a normal run.
var generatingOpenApi = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
if (generatingOpenApi)
{
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:swingtrade-db"] = "Host=openapi-generation-only",
        ["Telemetry:Log:Type"] = "console",
        ["Telemetry:Metrics:Type"] = "console",
        ["Telemetry:Trace:Type"] = "console",
        ["MarketData:Providers:0"] = "fake",
        ["Refresh:ScheduleEnabled"] = "false"
    });
}

builder.AddSpydersoftTelemetry(typeof(Program).Assembly,
    new ConfigurationFunctions
    {
        // Kubernetes probes hit these every few seconds; they add nothing but noise to traces.
        AspNetFilterFunction = context => !IsHealthCheckPath(context.Request.Path.Value)
    })
       .AddSpydersoftSerilog(true);

var healthCheckOptions = builder.AddSpydersoftHealthChecks();

builder.AddNpgsqlDbContext<SwingTradeDbContext>("swingtrade-db");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Auth:Authority"];
        // Both are for the Testing environment only (mock OIDC over http, one-second tokens to prove expiry); defaults are the secure ones.
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", true);
        options.ApplySwingTradeTokenValidation();
        if (builder.Configuration.GetValue<int?>("Auth:ClockSkewSeconds") is { } skewSeconds)
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(skewSeconds);
    });
builder.Services.AddSwingTradeAuthorization();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<InstrumentService>();
builder.Services.AddScoped<TradeService>();
builder.Services.AddScoped<NoteService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<WatchlistService>();
builder.Services.AddScoped<BarService>();
builder.Services.AddScoped<SizerService>();
builder.Services.AddScoped<ExportService>();

builder.Services.AddMarketData(builder.Configuration);
builder.Services.AddSingleton<MarketCalendar>();
builder.Services.AddSingleton<RefreshSchedule>();
builder.Services.AddSingleton<RefreshCoordinator>();
builder.Services.AddSingleton<IRefreshLauncher, RefreshLauncher>();
builder.Services.AddScoped<IngestionService>();
builder.Services.AddScoped<RefreshService>();
if (builder.Configuration.GetValue("Refresh:ScheduleEnabled", true))
    builder.Services.AddHostedService<RefreshHostedService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment() && app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MarketDataOptions>>().Value.Providers.Contains("fake", StringComparer.OrdinalIgnoreCase))
    app.Logger.LogWarning("The fake market data provider is configured outside Development; prices are synthetic.");

if (!generatingOpenApi)
{
    using var scope = app.Services.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>();
    await ctx.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(ctx, scope.ServiceProvider.GetRequiredService<TimeProvider>());
}

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
}

app.UseSpydersoftRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.UseSpydersoftHealthChecks(healthCheckOptions);

await app.RunAsync();

static bool IsHealthCheckPath(string? path) =>
    string.Equals(path, "/livez", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(path, "/readyz", StringComparison.OrdinalIgnoreCase);
