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
        options.ApplySwingTradeTokenValidation();
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

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>();
    await ctx.Database.MigrateAsync();
    await DatabaseSeeder.SeedAsync(ctx, scope.ServiceProvider.GetRequiredService<TimeProvider>());
}

if (app.Environment.IsDevelopment())
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
