namespace Spydersoft.SwingTrade.Contracts.Auth;

/// <summary>
/// Who the caller is and what the API will let them do, for the web UI. The UI uses it to hide or disable actions;
/// the API stays the only enforcement point. The role, policy and audience names are shown read-only in Settings.
/// </summary>
public sealed record AuthInfoDto(
    string? Subject,
    IReadOnlyList<string> Roles,
    bool CanRead,
    bool CanWrite,
    bool CanAdmin,
    string Audience,
    string ReaderRole,
    string TraderRole,
    string AdminRole,
    string McpRole,
    string ReadPolicy,
    string WritePolicy,
    string AdminPolicy,
    string McpPolicy);
