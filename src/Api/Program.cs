using System.Text;
using Api.Auth;
using Api.Categories;
using Api.Data;
using Api.Products;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// --- Database -------------------------------------------------------------
// Only business data (Categories, Products) lives here. Nothing about users or tokens is stored.
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// --- Settings -------------------------------------------------------------
// Make the "Jwt" and "ApiClient" sections available to other classes, and stop the
// app at startup with a clear message if a required value is missing.
builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<ApiClientOptions>()
    .BindConfiguration(ApiClientOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --- JWT ------------------------------------------------------------------
// Tell ASP.NET Core how to check the token on each incoming request.
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep claim names exactly as they appear in the token (for example "sub").
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            // The token must have been created by us...
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,

            // ...for us...
            ValidateAudience = true,
            ValidAudience = jwt.Audience,

            // ...signed with our key...
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),

            // ...and not expired. The default allows 5 extra minutes; 30 seconds is plenty.
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddSingleton<TokenService>();

// --- API plumbing ---------------------------------------------------------
builder.Services.AddProblemDetails(); // errors are returned as JSON in one standard shape
builder.Services.AddValidation();     // enforces rules like [Required] on request records
builder.Services.AddOpenApi();        // generates the API description document

var app = builder.Build();

// The order of the next lines matters: each request passes through them top to bottom.
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => TypedResults.Ok(new { status = "ok" })).ExcludeFromDescription();
app.MapAuthEndpoints();
app.MapCategoryEndpoints();
app.MapProductEndpoints();

app.Run();
