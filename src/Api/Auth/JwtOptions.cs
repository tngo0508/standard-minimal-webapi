using System.ComponentModel.DataAnnotations;

namespace Api.Auth;

/// <summary>
/// Bound from the "Jwt" configuration section. Issuer, Audience and ExpiryMinutes live in
/// appsettings.json; Key is a secret and comes from user-secrets (dev) or the
/// Jwt__Key environment variable (deployed).
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    // HMAC-SHA256 needs at least 256 bits of key material.
    [Required, MinLength(32)]
    public string Key { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpiryMinutes { get; set; } = 15;

    [Range(1, 365)]
    public int RefreshTokenDays { get; set; } = 14;
}
