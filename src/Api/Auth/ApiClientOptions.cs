using System.ComponentModel.DataAnnotations;

namespace Api.Auth;

/// <summary>
/// The one caller allowed to request tokens. Bound from the "ApiClient" configuration
/// section. ClientId lives in appsettings.json; ClientSecret is a secret and comes from
/// user-secrets (dev) or the ApiClient__ClientSecret environment variable (deployed).
/// </summary>
public class ApiClientOptions
{
    public const string SectionName = "ApiClient";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required, MinLength(16)]
    public string ClientSecret { get; set; } = string.Empty;
}
