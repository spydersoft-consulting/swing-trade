using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Spydersoft.SwingTrade.Api.Controllers;

namespace Spydersoft.SwingTrade.Api.UnitTests.Controllers;

/// <summary>The API table in docs/BUILD_PLAN.md, the controllers, and the committed OpenAPI document must agree.</summary>
public partial class ApiSurfaceTests
{
    // Every row of the plan's API table (the /api prefix is /api/v1 here, DECISIONS #8), plus the M3 extra (#51).
    private static readonly string[] PlanTable =
    [
        "GET /api/v1/settings", "PUT /api/v1/settings",
        "GET /api/v1/instruments", "POST /api/v1/instruments", "PUT /api/v1/instruments/{ticker}",
        "GET /api/v1/watchlist",
        "GET /api/v1/instruments/{ticker}/bars",
        "GET /api/v1/trades", "POST /api/v1/trades",
        "GET /api/v1/trades/{id}", "PUT /api/v1/trades/{id}", "DELETE /api/v1/trades/{id}",
        "POST /api/v1/trades/{id}/fill", "POST /api/v1/trades/{id}/close", "POST /api/v1/trades/{id}/cancel",
        "POST /api/v1/sizer",
        "GET /api/v1/dashboard",
        "GET /api/v1/notes", "POST /api/v1/notes",
        "GET /api/v1/refresh/runs", "POST /api/v1/refresh", "GET /api/v1/refresh/runs/{id}",
        "GET /api/v1/export/trades.csv",
        "GET /api/v1/mcp/info", "GET /api/v1/mcp/calls"
    ];

    [GeneratedRegex(@":[^}]+\}")]
    private static partial Regex Constraint();

    private static IEnumerable<string> ControllerRoutes()
    {
        var controllers = typeof(ApiControllerBase).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);
        foreach (var type in controllers)
        {
            var prefix = type.GetCustomAttribute<RouteAttribute>()!.Template;
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var http in method.GetCustomAttributes<HttpMethodAttribute>())
                {
                    var template = string.IsNullOrEmpty(http.Template) ? prefix : $"{prefix}/{http.Template}";
                    yield return $"{http.HttpMethods.Single()} /{Constraint().Replace(template, "}")}";
                }
            }
        }
    }

    private static string SpecPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "api", "openapi", "swing-trade.json");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("src/api/openapi/swing-trade.json not found; regenerate it (see src/api/README.md).");
    }

    [Test]
    public void EveryRowOfThePlanTable_IsImplementedByAController_AndNothingElseIs()
    {
        var actual = ControllerRoutes().ToList();

        Assert.That(actual, Is.EquivalentTo(PlanTable));
    }

    [Test]
    public void TheCommittedOpenApiDocument_ContainsEveryRouteAndVerb()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(SpecPath()));
        var inSpec = doc.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(p => p.Value.EnumerateObject().Select(v => $"{v.Name.ToUpperInvariant()} {p.Name}"))
            .ToList();

        Assert.That(inSpec, Is.EquivalentTo(PlanTable), "Regenerate with: dotnet build src/api/src/Spydersoft.SwingTrade.Api -p:GenerateOpenApi=true");
    }
}
