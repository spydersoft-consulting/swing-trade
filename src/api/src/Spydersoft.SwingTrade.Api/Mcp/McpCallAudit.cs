using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Spydersoft.SwingTrade.Data;
using Spydersoft.SwingTrade.Data.Entities;

namespace Spydersoft.SwingTrade.Api.Mcp;

public static class McpCallStatus
{
    public const string Ok = "ok";
    public const string Error = "error";
}

/// <summary>Makes tool arguments safe to store: sensitive keys and token-shaped strings are redacted, and the result is truncated.</summary>
public static partial class McpArgumentSanitizer
{
    public const int MaxStringLength = 200;
    public const int MaxTotalLength = 1000;
    public const string Redacted = "[redacted]";

    private static readonly string[] SensitiveKeys = ["token", "authorization", "bearer", "password", "secret", "apikey", "api_key", "cookie"];

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}(\.[A-Za-z0-9_\-]*)?")]
    private static partial Regex JwtShape();

    [GeneratedRegex(@"(?i)\bbearer\s+\S+")]
    private static partial Regex BearerShape();

    public static string? Sanitize(IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        if (arguments is null)
            return null;

        var node = new JsonObject();
        foreach (var (key, value) in arguments)
        {
            node[key] = SensitiveKeys.Any(s => key.Contains(s, StringComparison.OrdinalIgnoreCase))
                ? Redacted
                : Clean(JsonNode.Parse(value.GetRawText()));
        }

        var json = node.ToJsonString();
        return json.Length <= MaxTotalLength ? json : json[..MaxTotalLength] + "...";
    }

    /// <summary>For a free-text value such as a resource URI or prompt argument.</summary>
    public static string CleanText(string value) => Truncate(BearerShape().Replace(JwtShape().Replace(value, Redacted), Redacted));

    private static JsonNode? Clean(JsonNode? node)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                return JsonValue.Create(CleanText(s));
            case JsonObject o:
                var copy = new JsonObject();
                foreach (var (k, child) in o)
                {
                    copy[k] = SensitiveKeys.Any(x => k.Contains(x, StringComparison.OrdinalIgnoreCase)) ? Redacted : Clean(child?.DeepClone());
                }

                return copy;
            case JsonArray a:
                var list = new JsonArray();
                foreach (var child in a)
                    list.Add(Clean(child?.DeepClone()));
                return list;
            default:
                return node?.DeepClone();
        }
    }

    private static string Truncate(string s) => s.Length <= MaxStringLength ? s : s[..MaxStringLength] + "...";
}

/// <summary>Writes one McpCallLog row per tool, resource or prompt call. A logging failure never fails the call.</summary>
public sealed class McpCallRecorder(IServiceScopeFactory scopes, TimeProvider time, ILogger<McpCallRecorder> logger)
{
    public async ValueTask<TResult> RecordAsync<TResult>(
        string tool, string? arguments, string? caller, Func<ValueTask<TResult>> call, Func<TResult, string> statusOf)
    {
        var at = time.GetUtcNow();
        var clock = Stopwatch.StartNew();
        string status;
        try
        {
            var result = await call();
            status = statusOf(result);
            await WriteAsync(at, tool, arguments, clock, caller, status);
            return result;
        }
        catch
        {
            await WriteAsync(at, tool, arguments, clock, caller, McpCallStatus.Error);
            throw;
        }
    }

    private async Task WriteAsync(DateTimeOffset at, string tool, string? arguments, Stopwatch clock, string? caller, string status)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>();
            db.McpCallLogs.Add(new McpCallLog
            {
                At = at,
                Tool = Limit(tool, 100)!,
                Arguments = Limit(arguments, 4000),
                DurationMs = (int)Math.Min(clock.ElapsedMilliseconds, int.MaxValue),
                Caller = Limit(caller, 255),
                Status = status
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write the MCP call log entry for {Tool}", tool);
        }
    }

    private static string? Limit(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}

/// <summary>Request filters that audit every tools/call, resources/read and prompts/get.</summary>
public static class McpAuditFilters
{
    public static void Apply(Microsoft.Extensions.DependencyInjection.IMcpRequestFilterBuilder filters)
    {
        filters.AddCallToolFilter(next => (context, ct) =>
            Recorder(context.Services).RecordAsync(
                McpArgumentSanitizer.CleanText(context.Params?.Name ?? "(unknown tool)"),
                McpArgumentSanitizer.Sanitize(context.Params?.Arguments),
                Caller(context.User),
                () => next(context, ct),
                r => r.IsError == true ? McpCallStatus.Error : McpCallStatus.Ok));

        filters.AddReadResourceFilter(next => (context, ct) =>
            Recorder(context.Services).RecordAsync(
                "resource:" + McpArgumentSanitizer.CleanText(context.Params?.Uri ?? "(unknown)"),
                null,
                Caller(context.User),
                () => next(context, ct),
                _ => McpCallStatus.Ok));

        filters.AddGetPromptFilter(next => (context, ct) =>
            Recorder(context.Services).RecordAsync(
                "prompt:" + McpArgumentSanitizer.CleanText(context.Params?.Name ?? "(unknown)"),
                McpArgumentSanitizer.Sanitize(context.Params?.Arguments?.Select(a => new KeyValuePair<string, JsonElement>(a.Key, a.Value))),
                Caller(context.User),
                () => next(context, ct),
                _ => McpCallStatus.Ok));
    }

    private static McpCallRecorder Recorder(IServiceProvider? services) =>
        (services ?? throw new InvalidOperationException("No request services.")).GetRequiredService<McpCallRecorder>();

    private static string? Caller(System.Security.Claims.ClaimsPrincipal? user) => user?.FindFirst(SwingTradeAuth.SubjectClaimType)?.Value;
}

/// <summary>Deletes McpCallLog rows older than the retention period (90 days). Runs at startup and then every six hours.</summary>
public sealed class McpCallLogRetentionService(IServiceScopeFactory scopes, TimeProvider time, ILogger<McpCallLogRetentionService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    public static async Task<int> PurgeAsync(SwingTradeDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        var cutoff = now.AddDays(-McpOptions.RetentionDays);
        var old = await db.McpCallLogs.Where(l => l.At < cutoff).ToListAsync(ct);
        db.McpCallLogs.RemoveRange(old);
        await db.SaveChangesAsync(ct);
        return old.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var removed = await PurgeAsync(scope.ServiceProvider.GetRequiredService<SwingTradeDbContext>(), time.GetUtcNow(), stoppingToken);
                if (removed > 0)
                    logger.LogInformation("Removed {Count} MCP call log rows older than {Days} days", removed, McpOptions.RetentionDays);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "MCP call log retention failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
