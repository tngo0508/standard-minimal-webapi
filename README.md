# Standard Minimal Web API

A reference ASP.NET Core (.NET 10) Minimal API that uses SQL Server through EF Core and issues and validates its own
JWT bearer tokens.

## Layout

```
src/Api/
├── Program.cs              service registration and pipeline
├── Auth/                   JwtOptions, TokenService, /auth endpoints
├── Products/               /products CRUD endpoints (all require a token)
├── Data/                   AppDbContext and EF Core migrations
├── Entities/               User, Product
└── Api.http                sample requests
```

## First run

```powershell
dotnet tool restore
dotnet user-secrets set "Jwt:Key" "<at least 32 random characters>" --project src/Api
dotnet ef database update --project src/Api
dotnet run --project src/Api --launch-profile http
```

The API listens on `http://localhost:5106`. The OpenAPI document is at `/openapi/v1.json` in Development.

## Endpoints

| Method | Path             | Auth   | Purpose                           |
|--------|------------------|--------|-----------------------------------|
| POST   | `/auth/register` | none   | Create a user                     |
| POST   | `/auth/login`    | none   | Exchange credentials for an access token and a refresh token |
| POST   | `/auth/refresh`  | none   | Exchange a refresh token for a new pair |
| POST   | `/auth/logout`   | none   | Revoke a refresh token            |
| GET    | `/auth/me`       | bearer | Return the caller from the token  |
| GET    | `/products`      | bearer | List products                     |
| GET    | `/products/{id}` | bearer | Get one product                   |
| POST   | `/products`      | bearer | Create a product                  |
| PUT    | `/products/{id}` | bearer | Replace a product                 |
| DELETE | `/products/{id}` | bearer | Delete a product                  |

## Tokens

- **Access token**: a signed JWT, valid 15 minutes, sent as `Authorization: Bearer <token>`. It is never stored
  server-side and cannot be revoked; keeping it short-lived is what limits the damage if one leaks.
- **Refresh token**: an opaque random string, valid 14 days. Only its SHA-256 hash is stored, in the `RefreshTokens`
  table.
- **Rotation**: a refresh token works once. `/auth/refresh` revokes it and returns a new one.
- **Reuse detection**: presenting an already-used refresh token revokes every active refresh token for that user, so
  they must log in again.
- **Logout** revokes the refresh token it is given. The current access token still works until it expires.

Expired and revoked rows are never deleted; a real project should purge them on a schedule.

## Configuration

| Setting                     | Where                                       |
|-----------------------------|---------------------------------------------|
| `ConnectionStrings:Default` | `appsettings.json` (LocalDB by default)     |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes`, `Jwt:RefreshTokenDays` | `appsettings.json` |
| `Jwt:Key`                   | user-secrets locally, `Jwt__Key` env var when deployed; never in source control |

The app refuses to start if any `Jwt` setting is missing or the key is shorter than 32 characters.

## Changing the schema

```powershell
dotnet ef migrations add <Name> --project src/Api -o Data/Migrations
dotnet ef database update --project src/Api
```

## Deliberately left out

Roles/policies, account lockout, cleanup of old refresh tokens, rate limiting on `/auth`, and automated tests. Add what your real
project needs.
