namespace Spydersoft.SwingTrade.Api;

/// <summary>Single source of truth for the audience, role names, and policy names. Shown read-only in Settings.</summary>
public static class SwingTradeAuth
{
    public const string Audience = "swingtrade";
    public const string RoleClaimType = "role";
    public const string SubjectClaimType = "sub";
    public const string AudienceClaimType = "aud";

    public static class Roles
    {
        public const string Reader = "swingtrade.reader";
        public const string Trader = "swingtrade.trader";
        public const string Admin = "swingtrade.admin";
        public const string Mcp = "swingtrade.mcp";
    }

    public static class Policies
    {
        public const string Read = "Read";
        public const string Write = "Write";
        public const string Admin = "Admin";
        public const string Mcp = "Mcp";
        /// <summary>Trader or admin, with the swingtrade audience or the MCP resource URL. Applied to the MCP write tools.</summary>
        public const string McpWrite = "McpWrite";
        /// <summary>The Mcp role plus the configured owner subject. Applied to the /mcp route.</summary>
        public const string McpOwner = "McpOwner";
    }
}
