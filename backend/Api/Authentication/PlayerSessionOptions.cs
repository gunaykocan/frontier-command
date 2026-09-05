namespace Game.Api.Authentication;

public sealed class PlayerSessionOptions
{
    public const string SectionName = "PlayerSessions";

    public string CookieName { get; set; } = "frontier-command-session";

    public int LifetimeHours { get; set; } = 168;

    public string? KeyDirectory { get; set; }

    public TimeSpan Lifetime => TimeSpan.FromHours(LifetimeHours);
}
