using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Api.Data;
using Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Api.Auth;

public record RegisterRequest(
    [property: Required, EmailAddress, MaxLength(256)] string Email,
    [property: Required, MinLength(8), MaxLength(128)] string Password);

public record LoginRequest(
    [property: Required] string Email,
    [property: Required] string Password);

public record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public record UserResponse(int Id, string Email);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", Register);
        group.MapPost("/login", Login);
        group.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);

        if (await db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Email is already registered.");
        }

        var user = new User { Email = email };
        user.PasswordHash = hasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return Results.Created("/auth/me", new UserResponse(user.Id, user.Email));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        AppDbContext db,
        IPasswordHasher<User> hasher,
        TokenService tokens,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        // Same response for "no such user" and "wrong password" so the endpoint
        // does not reveal which emails are registered.
        if (user is null)
        {
            return InvalidCredentials();
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return InvalidCredentials();
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        var token = tokens.CreateAccessToken(user);
        return Results.Ok(new TokenResponse(token.Value, "Bearer", token.ExpiresAt));
    }

    private static IResult Me(ClaimsPrincipal principal)
    {
        var id = int.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email)!;
        return Results.Ok(new UserResponse(id, email));
    }

    private static IResult InvalidCredentials() =>
        Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid email or password.");

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
