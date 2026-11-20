using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Api.Auth;

public record AccessToken(string Value, DateTimeOffset ExpiresAt);

public class TokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _jwt = options.Value;

    public AccessToken CreateAccessToken(string clientId)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_jwt.ExpiryMinutes);

        // The secret key that signs the token. Program.cs uses the same key to check it.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Key));

        // Everything that goes into the token.
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,       // who created the token
            Audience = _jwt.Audience,   // who the token is for
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, clientId),                  // who is calling
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), // unique id of this token
            ]),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        // Turn the description into the signed text: header.payload.signature
        var handler = new JsonWebTokenHandler();
        string tokenText = handler.CreateToken(descriptor);

        return new AccessToken(tokenText, expires);
    }
}
