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

public record RefreshRequest([property: Required, MaxLength(256)] string RefreshToken);

public record TokenResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public record UserResponse(int Id, string Email);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/register", Register);
        group.MapPost("/login", Login);
        group.MapPost("/refresh", Refresh);
        group.MapPost("/logout", Logout);
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
        TimeProvider time,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);

        // Same response for "no such user" and "wrong password" so the endpoint
        // does not reveal which emails are registered.
        if (user is null)
        {
            return Unauthorized("Invalid email or password.");
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized("Invalid email or password.");
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        }

        return Results.Ok(await IssueTokensAsync(user, db, tokens, time, ct));
    }

    /// <summary>
    /// Exchanges a refresh token for a new access token and a new refresh token.
    /// Refresh tokens are single-use: each call revokes the one presented (rotation).
    /// </summary>
    private static async Task<IResult> Refresh(
        RefreshRequest request,
        AppDbContext db,
        TokenService tokens,
        TimeProvider time,
        CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(request.RefreshToken);
        var stored = await db.RefreshTokens
            .Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
        {
            return Unauthorized("Invalid refresh token.");
        }

        DateTimeOffset? now = time.GetUtcNow();

        if (stored.RevokedAt is not null)
        {
            // An already-used token is being replayed. Either the client or a thief
            // holds a stale copy, and we cannot tell which, so end every session
            // for this user and force a fresh login.
            await db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

            return Unauthorized("Invalid refresh token.");
        }

        if (stored.ExpiresAt <= now)
        {
            return Unauthorized("Invalid refresh token.");
        }

        // Claim the token atomically so two concurrent requests cannot both rotate it.
        var claimed = await db.RefreshTokens
            .Where(t => t.Id == stored.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        if (claimed == 0)
        {
            return Unauthorized("Invalid refresh token.");
        }

        return Results.Ok(await IssueTokensAsync(stored.User, db, tokens, time, ct));
    }

    /// <summary>
    /// Revokes the presented refresh token. Always 204, whether or not the token was
    /// known. The current access token stays valid until it expires, which is why
    /// access tokens are short-lived.
    /// </summary>
    private static async Task<IResult> Logout(
        RefreshRequest request,
        AppDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var hash = TokenService.HashRefreshToken(request.RefreshToken);
        DateTimeOffset? now = time.GetUtcNow();

        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        return Results.NoContent();
    }

    private static IResult Me(ClaimsPrincipal principal)
    {
        var id = int.Parse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email)!;
        return Results.Ok(new UserResponse(id, email));
    }

    private static async Task<TokenResponse> IssueTokensAsync(
        User user,
        AppDbContext db,
        TokenService tokens,
        TimeProvider time,
        CancellationToken ct)
    {
        var access = tokens.CreateAccessToken(user);
        var refresh = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.Hash,
            CreatedAt = time.GetUtcNow(),
            ExpiresAt = refresh.ExpiresAt,
        });
        await db.SaveChangesAsync(ct);

        return new TokenResponse(access.Value, "Bearer", access.ExpiresAt, refresh.Value, refresh.ExpiresAt);
    }

    private static IResult Unauthorized(string title) =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: title);

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
