using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Spydersoft.SwingTrade.MarketData;

public static class MarketDataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the providers, binds <see cref="MarketDataOptions"/> from the "MarketData" section and validates it at
    /// startup (unknown provider name or a missing secret fails fast). The scoped <see cref="IBarFetcher"/> is a
    /// <see cref="FallbackMarketDataProvider"/> over the configured providers in order.
    /// </summary>
    public static IServiceCollection AddMarketData(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MarketDataOptions>()
            .Bind(configuration.GetSection(MarketDataOptions.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MarketDataOptions>, MarketDataOptionsValidator>());

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDelay, TaskDelay>();

        services.AddHttpClient<AlpacaMarketDataProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<TiingoMarketDataProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddSingleton<FakeMarketDataProvider>();

        services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<AlpacaMarketDataProvider>());
        services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<TiingoMarketDataProvider>());
        services.AddTransient<IMarketDataProvider>(sp => sp.GetRequiredService<FakeMarketDataProvider>());

        services.AddScoped<FallbackMarketDataProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MarketDataOptions>>();
            var available = sp.GetServices<IMarketDataProvider>().ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            var ordered = options.Value.Providers.Select(n => available[n.Trim()]).ToList();
            return new FallbackMarketDataProvider(
                ordered, options, sp.GetRequiredService<IDelay>(), sp.GetRequiredService<TimeProvider>(),
                sp.GetService<ILogger<FallbackMarketDataProvider>>());
        });
        services.AddScoped<IBarFetcher>(sp => sp.GetRequiredService<FallbackMarketDataProvider>());

        return services;
    }
}

/// <summary>Fails fast on an unknown provider name or a missing secret for a configured provider.</summary>
public sealed class MarketDataOptionsValidator : IValidateOptions<MarketDataOptions>
{
    public static readonly string[] KnownProviders =
        [AlpacaMarketDataProvider.ProviderName, TiingoMarketDataProvider.ProviderName, FakeMarketDataProvider.ProviderName];

    public ValidateOptionsResult Validate(string? name, MarketDataOptions options)
    {
        var errors = new List<string>();
        if (options.Providers.Count == 0)
            errors.Add("MarketData:Providers must list at least one provider.");
        if (options.MaxAttempts < 1)
            errors.Add("MarketData:MaxAttempts must be at least 1.");

        foreach (var raw in options.Providers)
        {
            var provider = raw?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!KnownProviders.Contains(provider))
                errors.Add($"Unknown market data provider '{raw}'. Known: {string.Join(", ", KnownProviders)}.");
            else if (provider == AlpacaMarketDataProvider.ProviderName &&
                     (string.IsNullOrWhiteSpace(options.Alpaca.KeyId) || string.IsNullOrWhiteSpace(options.Alpaca.SecretKey)))
                errors.Add("Provider 'alpaca' needs MarketData__Alpaca__KeyId and MarketData__Alpaca__SecretKey (secrets).");
            else if (provider == TiingoMarketDataProvider.ProviderName && string.IsNullOrWhiteSpace(options.Tiingo.Token))
                errors.Add("Provider 'tiingo' needs MarketData__Tiingo__Token (secret).");
        }

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
