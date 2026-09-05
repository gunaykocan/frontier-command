using Game.Api.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Game.Api.Tests;

public sealed class PlayerSessionTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IssuedTokenRestoresPlayerAndMatchIdentity()
    {
        var timeProvider = new TestTimeProvider(Now);
        var service = CreateService(timeProvider);
        var playerId = Guid.NewGuid();
        var matchId = Guid.NewGuid();

        var token = service.Issue(playerId, matchId);

        Assert.True(service.TryValidate(token.Value, out var identity));
        Assert.Equal(playerId, identity.PlayerId);
        Assert.Equal(matchId, identity.MatchId);
        Assert.Equal(Now.AddHours(24), token.ExpiresAtUtc);
    }

    [Fact]
    public void TamperedTokenIsRejected()
    {
        var service = CreateService(new TestTimeProvider(Now));
        var token = service.Issue(Guid.NewGuid(), Guid.NewGuid()).Value;
        var characters = token.ToCharArray();
        var index = characters.Length / 2;
        characters[index] = characters[index] == 'A' ? 'B' : 'A';

        var isValid = service.TryValidate(new string(characters), out _);

        Assert.False(isValid);
    }

    [Fact]
    public void ExpiredTokenIsRejected()
    {
        var timeProvider = new TestTimeProvider(Now);
        var service = CreateService(timeProvider);
        var token = service.Issue(Guid.NewGuid(), Guid.NewGuid());

        timeProvider.Advance(TimeSpan.FromHours(25));

        Assert.False(service.TryValidate(token.Value, out _));
    }

    [Fact]
    public void SessionCookieIsHttpOnlyAndSameSite()
    {
        var options = CreateOptions();
        var service = CreateService(new TestTimeProvider(Now), options);
        var cookieService = new PlayerSessionCookieService(service, options);
        var context = new DefaultHttpContext();

        cookieService.Issue(context.Response, isHttps: true, Guid.NewGuid(), Guid.NewGuid());

        var setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("frontier-command-session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClearingSessionExpiresTheExistingCookieForTheWholeApplication()
    {
        var options = CreateOptions();
        var service = CreateService(new TestTimeProvider(Now), options);
        var cookieService = new PlayerSessionCookieService(service, options);
        var context = new DefaultHttpContext();

        cookieService.Clear(context.Response, isHttps: false);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("frontier-command-session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    private static PlayerSessionTokenService CreateService(
        TimeProvider timeProvider,
        IOptions<PlayerSessionOptions>? options = null) => new(
            new EphemeralDataProtectionProvider(),
            options ?? CreateOptions(),
            timeProvider);

    private static IOptions<PlayerSessionOptions> CreateOptions() => Options.Create(
        new PlayerSessionOptions
        {
            CookieName = "frontier-command-session",
            LifetimeHours = 24
        });

    private sealed class TestTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
