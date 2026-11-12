using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BarberHub.Api.Domain.Entities;
using BarberHub.Api.Infrastructure.Persistence;
using BarberHub.Api.Shared.Config;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BarberHub.Api.Shared.Services;

public class TokenPairResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
}

public interface IJwtTokenService
{
    /// <summary>Creates a new access + refresh token pair for the user and persists the refresh token (hashed).</summary>
    Task<TokenPairResult> IssueTokenPairAsync(ApplicationUser user, IList<string> roles, string? deviceInfo = null, string? ipAddress = null);

    /// <summary>Validates the given refresh token, rotates it (revokes old, issues new), and returns a new pair.
    /// Returns null if the token is invalid, expired, revoked, or reused (theft-detection: reuse of an already-rotated token revokes the whole chain).</summary>
    Task<TokenPairResult?> RefreshAsync(string refreshToken, string? deviceInfo = null, string? ipAddress = null);

    /// <summary>Revokes a single refresh token (e.g. logout on this device).</summary>
    Task RevokeAsync(string refreshToken);

    /// <summary>Revokes every refresh token for a user (e.g. "log out all devices", or password change).</summary>
    Task RevokeAllForUserAsync(string userId);

    /// <summary>Short-lived (10 min) token used to carry the user through a 2FA challenge without a full session.</summary>
    string IssueTwoFactorChallengeToken(string userId);
    string? ValidateTwoFactorChallengeToken(string token);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public JwtTokenService(IOptions<JwtOptions> options, ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _options = options.Value;
        _db = db;
        _userManager = userManager;
    }

    public async Task<TokenPairResult> IssueTokenPairAsync(ApplicationUser user, IList<string> roles, string? deviceInfo = null, string? ipAddress = null)
    {
        var (accessToken, expiresAt) = CreateAccessToken(user, roles);
        var refreshTokenPlain = GenerateSecureToken();

        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refreshTokenPlain),
            ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
            DeviceInfo = deviceInfo,
            IpAddress = ipAddress
        };
        await _db.RefreshTokens.AddAsync(entity);
        await _db.SaveChangesAsync();

        return new TokenPairResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenPlain,
            AccessTokenExpiresAt = expiresAt
        };
    }

    public async Task<TokenPairResult?> RefreshAsync(string refreshToken, string? deviceInfo = null, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;
        var hash = Hash(refreshToken);

        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (existing is null) return null;

        // Reuse of an already-revoked token = possible theft. Revoke the whole chain for this user.
        if (existing.RevokedAt is not null || existing.ExpiresAt <= DateTime.UtcNow)
        {
            if (existing.RevokedAt is not null)
                await RevokeAllForUserAsync(existing.UserId);
            return null;
        }

        var user = await _userManager.FindByIdAsync(existing.UserId);
        if (user is null || user.IsDeleted || !user.IsActive) return null;

        var roles = await _userManager.GetRolesAsync(user);
        var (accessToken, expiresAt) = CreateAccessToken(user, roles);
        var newRefreshPlain = GenerateSecureToken();

        var newEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(newRefreshPlain),
            ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
            DeviceInfo = deviceInfo ?? existing.DeviceInfo,
            IpAddress = ipAddress ?? existing.IpAddress
        };
        await _db.RefreshTokens.AddAsync(newEntity);

        existing.RevokedAt = DateTime.UtcNow;
        existing.ReplacedByTokenId = newEntity.Id;

        await _db.SaveChangesAsync();

        return new TokenPairResult
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshPlain,
            AccessTokenExpiresAt = expiresAt
        };
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var hash = Hash(refreshToken);
        var existing = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (existing is null || existing.RevokedAt is not null) return;
        existing.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task RevokeAllForUserAsync(string userId)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync();
        foreach (var t in tokens) t.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public string IssueTwoFactorChallengeToken(string userId)
    {
        var claims = new[]
        {
            new Claim("uid", userId),
            new Claim("purpose", "2fa-challenge")
        };
        var creds = new SigningCredentials(SigningKeyObj, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string? ValidateTwoFactorChallengeToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, ValidationParameters, out _);
            if (principal.FindFirstValue("purpose") != "2fa-challenge") return null;
            return principal.FindFirstValue("uid");
        }
        catch
        {
            return null;
        }
    }

    private (string token, DateTime expiresAt) CreateAccessToken(ApplicationUser user, IList<string> roles)
    {
        var expiresAt = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.FullName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var creds = new SigningCredentials(SigningKeyObj, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    private SymmetricSecurityKey SigningKeyObj =>
        new(Encoding.UTF8.GetBytes(_options.SigningKey));

    private TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _options.Issuer,
        ValidateAudience = true,
        ValidAudience = _options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKeyObj,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    private static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string input)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
    }
}
