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
| POST   | `/auth/login`    | none   | Exchange credentials for a JWT    |
| GET    | `/auth/me`       | bearer | Return the caller from the token  |
| GET    | `/products`      | bearer | List products                     |
| GET    | `/products/{id}` | bearer | Get one product                   |
| POST   | `/products`      | bearer | Create a product                  |
| PUT    | `/products/{id}` | bearer | Replace a product                 |
| DELETE | `/products/{id}` | bearer | Delete a product                  |

## Configuration

| Setting                     | Where                                       |
|-----------------------------|---------------------------------------------|
| `ConnectionStrings:Default` | `appsettings.json` (LocalDB by default)     |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpiryMinutes` | `appsettings.json`    |
| `Jwt:Key`                   | user-secrets locally, `Jwt__Key` env var when deployed; never in source control |

The app refuses to start if any `Jwt` setting is missing or the key is shorter than 32 characters.

## Changing the schema

```powershell
dotnet ef migrations add <Name> --project src/Api -o Data/Migrations
dotnet ef database update --project src/Api
```

## Deliberately left out

Refresh tokens, roles/policies, account lockout, rate limiting on `/auth`, and automated tests. Add what your real
project needs.
