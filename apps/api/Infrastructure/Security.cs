using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using GoldenHour.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GoldenHour.Api.Infrastructure;

public sealed class AuthenticationOptions
{
    public JwtOptions Jwt { get; set; } = new();
    public int AccessTokenMinutes { get; set; } = 10;
    public int RefreshTokenDays { get; set; } = 7;
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "GoldenHourAI";
    public string Audience { get; set; } = "GoldenHourAI.Web";
    public string SigningKey { get; set; } = string.Empty;
}

public sealed record AuthTokenPair(string AccessToken, DateTime AccessExpiresAtUtc, string RefreshToken, DateTime RefreshExpiresAtUtc);

public sealed class RefreshTokenReuseException : Exception
{
    public RefreshTokenReuseException() : base("Refresh token reuse was detected; the token family was revoked.") { }
}

public sealed class TokenService(
    GoldenHourDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IOptions<AuthenticationOptions> options,
    TimeProvider timeProvider)
{
    public async Task<AuthTokenPair> IssueAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var familyId = Guid.NewGuid();
        var refresh = CreateRefreshToken(user.Id, familyId);
        dbContext.RefreshTokens.Add(refresh.Entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await CreatePairAsync(user, refresh.RawToken, refresh.Entity.ExpiresAtUtc);
    }

    public async Task<AuthTokenPair> RotateAsync(string rawToken, CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        var current = await dbContext.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash.SequenceEqual(hash), cancellationToken)
            ?? throw new SecurityTokenException("Invalid refresh token.");
        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (current.RevokedAtUtc is not null)
        {
            dbContext.AuditEvents.Add(new AuditEvent
            {
                Action = "refresh-token-reuse-detected",
                ResourceType = "RefreshTokenFamily",
                ResourceId = current.FamilyId.ToString()
            });
            await RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            throw new RefreshTokenReuseException();
        }

        if (current.ExpiresAtUtc <= now)
        {
            throw new SecurityTokenExpiredException("Refresh token expired.");
        }

        var user = await userManager.FindByIdAsync(current.UserId.ToString())
            ?? throw new SecurityTokenException("Refresh token owner no longer exists.");
        var replacement = CreateRefreshToken(user.Id, current.FamilyId);
        current.RevokedAtUtc = now;
        current.ReplacedByTokenId = replacement.Entity.Id;
        dbContext.RefreshTokens.Add(replacement.Entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await CreatePairAsync(user, replacement.RawToken, replacement.Entity.ExpiresAtUtc);
    }

    public async Task RevokeAsync(string? rawToken, Guid userId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (!string.IsNullOrWhiteSpace(rawToken))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
            var token = await dbContext.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash.SequenceEqual(hash) && x.UserId == userId, cancellationToken);
            if (token is not null)
            {
                token.RevokedAtUtc ??= now;
            }
        }
        else
        {
            var tokens = await dbContext.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.RevokedAtUtc = now;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthTokenPair> CreatePairAsync(ApplicationUser user, string refreshToken, DateTime refreshExpiresAtUtc)
    {
        var configured = options.Value;
        if (Encoding.UTF8.GetByteCount(configured.Jwt.SigningKey) < 32)
        {
            throw new InvalidOperationException("JWT signing key must contain at least 32 UTF-8 bytes.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var accessExpiry = now.AddMinutes(configured.AccessTokenMinutes);
        var roles = await userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configured.Jwt.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            configured.Jwt.Issuer,
            configured.Jwt.Audience,
            claims,
            now,
            accessExpiry,
            credentials);
        return new AuthTokenPair(new JwtSecurityTokenHandler().WriteToken(jwt), accessExpiry, refreshToken, refreshExpiresAtUtc);
    }

    private (RefreshToken Entity, string RawToken) CreateRefreshToken(Guid userId, Guid familyId)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var entity = new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(raw)),
            ExpiresAtUtc = timeProvider.GetUtcNow().UtcDateTime.AddDays(options.Value.RefreshTokenDays)
        };
        return (entity, raw);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken cancellationToken)
    {
        var family = await dbContext.RefreshTokens.Where(x => x.FamilyId == familyId && x.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var token in family)
        {
            token.RevokedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public static class AuthCookies
{
    public const string Access = "gh_access";
    public const string Refresh = "gh_refresh";

    public static void Set(HttpResponse response, AuthTokenPair pair, bool secure)
    {
        response.Cookies.Append(Access, pair.AccessToken, CookieOptions(pair.AccessExpiresAtUtc, secure, "/"));
        response.Cookies.Append(Refresh, pair.RefreshToken, CookieOptions(pair.RefreshExpiresAtUtc, secure, "/api/v1/auth"));
    }

    public static void Clear(HttpResponse response, bool secure)
    {
        response.Cookies.Delete(Access, CookieOptions(DateTime.UnixEpoch, secure, "/"));
        response.Cookies.Delete(Refresh, CookieOptions(DateTime.UnixEpoch, secure, "/api/v1/auth"));
    }

    private static CookieOptions CookieOptions(DateTime expiresUtc, bool secure, string path) => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
        Path = path,
        Expires = new DateTimeOffset(expiresUtc, TimeSpan.Zero)
    };
}
