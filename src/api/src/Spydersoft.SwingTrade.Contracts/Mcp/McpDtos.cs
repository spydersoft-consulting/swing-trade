namespace Spydersoft.SwingTrade.Contracts.Mcp;

/// <summary>What the Settings MCP panel shows. The owner is identified by subject only; no token or email is exposed.</summary>
public sealed record McpInfoDto(
    string? EndpointUrl,
    string? OwnerSubject,
    bool AddNoteEnabled,
    string Audience,
    string RequiredRole,
    string AddNoteRole);

/// <summary>One audited MCP call. Arguments are sanitised and truncated; tokens are never stored.</summary>
public sealed record McpCallDto(
    int Id,
    DateTimeOffset At,
    string Tool,
    string? Arguments,
    int DurationMs,
    string? Caller,
    string Status);
