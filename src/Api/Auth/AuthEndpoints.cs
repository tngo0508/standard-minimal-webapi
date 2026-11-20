using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Api.Auth;

public record LoginRequest(
    [Required] string ClientId,
    [Required] string ClientSecret);

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

        var valid = request.ClientId == client.ClientId
                    && request.ClientSecret == client.ClientSecret;

        // Same message whichever part was wrong, so the reply gives no hint.
        if (!valid)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid client id or secret.");
        }

        var token = tokens.CreateAccessToken(client.ClientId);
        var response = new TokenResponse(token.Value, "Bearer", token.ExpiresAt);

        return TypedResults.Ok(response);
    }

    /// <summary>Shows who the token belongs to, read from the token's "sub" claim.</summary>
    private static Ok<ClientResponse> Me(ClaimsPrincipal caller)
    {
        // RequireAuthorization() guarantees a valid token, and every token we issue has "sub".
        var clientId = caller.FindFirstValue(JwtRegisteredClaimNames.Sub)!;

        return TypedResults.Ok(new ClientResponse(clientId));
    }
}
