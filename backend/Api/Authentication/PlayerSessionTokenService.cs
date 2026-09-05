using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Game.Api.Authentication;

public sealed record PlayerSessionIdentity(Guid PlayerId, Guid MatchId);

public sealed record IssuedPlayerSessionToken(string Value, DateTimeOffset ExpiresAtUtc);

public interface IPlayerSessionTokenService
{
    IssuedPlayerSessionToken Issue(Guid playerId, Guid matchId);

    bool TryValidate(
        string token,
        [NotNullWhen(true)] out PlayerSessionIdentity? identity);
}

public sealed class PlayerSessionTokenService : IPlayerSessionTokenService
{
    private const int CurrentVersion = 1;
    private readonly IDataProtector _protector;
    private readonly PlayerSessionOptions _options;
    private readonly TimeProvider _timeProvider;

    public PlayerSessionTokenService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PlayerSessionOptions> options,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(
            "Game.Api.Authentication.PlayerSession",
            $"v{CurrentVersion}");
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public IssuedPlayerSessionToken Issue(Guid playerId, Guid matchId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(playerId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(matchId, Guid.Empty);

        var expiresAtUtc = _timeProvider.GetUtcNow().Add(_options.Lifetime);
        var payload = new PlayerSessionTokenPayload(
            CurrentVersion,
            playerId,
            matchId,
            expiresAtUtc);
        var value = _protector.Protect(JsonSerializer.Serialize(payload));

        return new IssuedPlayerSessionToken(value, expiresAtUtc);
    }

    public bool TryValidate(
        string token,
        [NotNullWhen(true)] out PlayerSessionIdentity? identity)
    {
        identity = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        try
        {
            var json = _protector.Unprotect(token);
            var payload = JsonSerializer.Deserialize<PlayerSessionTokenPayload>(json);

            if (payload is null
                || payload.Version != CurrentVersion
                || payload.PlayerId == Guid.Empty
                || payload.MatchId == Guid.Empty
                || payload.ExpiresAtUtc <= _timeProvider.GetUtcNow())
            {
                return false;
            }

            identity = new PlayerSessionIdentity(payload.PlayerId, payload.MatchId);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record PlayerSessionTokenPayload(
        int Version,
        Guid PlayerId,
        Guid MatchId,
        DateTimeOffset ExpiresAtUtc);
}
