namespace Client;

// These records describe the JSON the API sends and receives.
//
// They are written again here rather than shared with the API project on purpose.
// A real client is usually a separate program, often in another language, and knows
// the API only through its documented JSON, never through its source code.

public record LoginRequest(string ClientId, string ClientSecret);

public record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public record ClientInfo(string ClientId);

public record CategoryRequest(string Name);

public record Category(int Id, string Name);

public record ProductRequest(string Name, decimal Price, int? CategoryId = null);

public record Product(int Id, string Name, decimal Price, int? CategoryId, string? CategoryName);

/// <summary>One page of results from GET /products/search.</summary>
public record ProductSearchResult(List<Product> Items, int Page, int PageSize, int TotalCount, int TotalPages);

/// <summary>The shape of every error the API returns (Problem Details).</summary>
public record Problem(string? Title, int? Status, Dictionary<string, string[]>? Errors);
