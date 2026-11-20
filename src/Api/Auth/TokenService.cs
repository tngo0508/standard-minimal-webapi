using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Api.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Auth;

public record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <param name="Value">The raw token, returned to the client once and never stored.</param>
/// <param name="Hash">What gets stored and looked up.</param>
public record NewRefreshToken(string Value, string Hash, DateTimeOffset ExpiresAt);

public class TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private readonly JwtOptions _jwt = options.Value;

    public AccessToken CreateAccessToken(User user)
    {
        var now = timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_jwt.ExpiryMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key)),
                SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    /// <summary>
    /// A refresh token is an opaque random string, not a JWT: it carries no claims
    /// and is only meaningful as a lookup key in the RefreshTokens table.
    /// </summary>
    public NewRefreshToken CreateRefreshToken()
    {
        var value = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expires = timeProvider.GetUtcNow().AddDays(_jwt.RefreshTokenDays);
        return new NewRefreshToken(value, HashRefreshToken(value), expires);
    }

    public static string HashRefreshToken(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
