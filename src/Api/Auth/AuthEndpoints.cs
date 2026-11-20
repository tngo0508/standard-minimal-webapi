using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Api.Auth;

public record LoginRequest(
    [property: Required] string ClientId,
    [property: Required] string ClientSecret);

public record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public record ClientResponse(string ClientId);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", Login).ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Exchanges the configured client id and secret for an access token.
    /// Nothing is read from or written to the database.
    /// </summary>
    private static Results<Ok<TokenResponse>, ProblemHttpResult> Login(
        LoginRequest request,
        IOptions<ApiClientOptions> clientOptions,
        TokenService tokens)
    {
        var client = clientOptions.Value;

        // Check both values every time (& rather than &&) and compare in constant time,
        // so response timing does not hint at which part was wrong.
        var valid = FixedTimeEquals(request.ClientId, client.ClientId)
                    & FixedTimeEquals(request.ClientSecret, client.ClientSecret);

        if (!valid)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid client id or secret.");
        }

        var token = tokens.CreateAccessToken(client.ClientId);
        return TypedResults.Ok(new TokenResponse(token.Value, "Bearer", token.ExpiresAt));
    }

    /// <summary>Shows who the token belongs to, read from the token's "sub" claim.</summary>
    private static Ok<ClientResponse> Me(ClaimsPrincipal principal) =>
        TypedResults.Ok(new ClientResponse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
