using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Client;

/// <summary>
/// One method per API endpoint. Callers never deal with tokens, headers or JSON:
/// this class logs in when needed and attaches the token to every request.
/// </summary>
public class ApiClient(HttpClient http, string clientId, string clientSecret)
{
    // The token from the last login. Null until we log in for the first time.
    private TokenResponse? _token;

    // ---- Auth -----------------------------------------------------------

    /// <summary>Exchanges the client id and secret for a token and remembers it.</summary>
    public async Task<TokenResponse> LoginAsync()
    {
        var request = new LoginRequest(clientId, clientSecret);

        var response = await http.PostAsJsonAsync("/auth/login", request);
        await ThrowIfFailedAsync(response);

        _token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        return _token!;
    }

    public async Task<ClientInfo> GetMeAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/auth/me");
        return (await response.Content.ReadFromJsonAsync<ClientInfo>())!;
    }

    // ---- Categories -----------------------------------------------------

    public async Task<List<Category>> GetCategoriesAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/categories");
        return (await response.Content.ReadFromJsonAsync<List<Category>>())!;
    }

    public async Task<Category> CreateCategoryAsync(string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/categories", new CategoryRequest(name));
        return (await response.Content.ReadFromJsonAsync<Category>())!;
    }

    public async Task<Category> RenameCategoryAsync(int id, string name)
    {
        var response = await SendAsync(HttpMethod.Put, $"/categories/{id}", new CategoryRequest(name));
        return (await response.Content.ReadFromJsonAsync<Category>())!;
    }

    public async Task DeleteCategoryAsync(int id)
    {
        await SendAsync(HttpMethod.Delete, $"/categories/{id}");
    }

    // ---- Products -------------------------------------------------------

    public async Task<List<Product>> GetProductsAsync()
    {
        var response = await SendAsync(HttpMethod.Get, "/products");
        return (await response.Content.ReadFromJsonAsync<List<Product>>())!;
    }

    public async Task<List<Product>> GetProductsInCategoryAsync(int categoryId)
    {
        var response = await SendAsync(HttpMethod.Get, $"/products?categoryId={categoryId}");
        return (await response.Content.ReadFromJsonAsync<List<Product>>())!;
    }

    /// <summary>Finds products whose name contains the text, one page at a time.</summary>
    public async Task<ProductSearchResult> SearchProductsAsync(string name, int page, int pageSize)
    {
        // EscapeDataString makes the text safe to put in a URL, for example a space becomes %20.
        var encodedName = Uri.EscapeDataString(name);
        var path = $"/products/search?name={encodedName}&page={page}&pageSize={pageSize}";

        var response = await SendAsync(HttpMethod.Get, path);
        return (await response.Content.ReadFromJsonAsync<ProductSearchResult>())!;
    }

    public async Task<Product> GetProductAsync(int id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/products/{id}");
        return (await response.Content.ReadFromJsonAsync<Product>())!;
    }

    public async Task<Product> CreateProductAsync(ProductRequest product)
    {
        var response = await SendAsync(HttpMethod.Post, "/products", product);
        return (await response.Content.ReadFromJsonAsync<Product>())!;
    }

    public async Task<Product> UpdateProductAsync(int id, ProductRequest product)
    {
        var response = await SendAsync(HttpMethod.Put, $"/products/{id}", product);
        return (await response.Content.ReadFromJsonAsync<Product>())!;
    }

    public async Task DeleteProductAsync(int id)
    {
        await SendAsync(HttpMethod.Delete, $"/products/{id}");
    }

    // ---- The one place every protected request goes through --------------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        // 1. Make sure we have a token that is not about to expire.
        var needsLogin = _token is null || _token.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30);
        if (needsLogin)
        {
            await LoginAsync();
        }

        // 2. Build the request and attach the token.
        //    This header is the whole of "being logged in": Authorization: Bearer <token>
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        // 3. Send it, and turn an error response into an exception.
        var response = await http.SendAsync(request);
        await ThrowIfFailedAsync(response);

        return response;
    }

    /// <summary>Does nothing on success. On failure, throws an ApiException carrying the API's own message.</summary>
    private static async Task ThrowIfFailedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Start with the plain status text, for example "Not Found".
        var title = response.ReasonPhrase ?? "Request failed";
        Dictionary<string, string[]>? errors = null;

        // The API normally sends a JSON body with more detail. Use it when it is there.
        var isJson = response.Content.Headers.ContentType?.MediaType?.EndsWith("json") == true;
        if (isJson)
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>();
            if (problem?.Title is not null)
            {
                title = problem.Title;
            }

            errors = problem?.Errors;
        }

        throw new ApiException(response.StatusCode, title, errors);
    }
}

/// <summary>Thrown when the API answers with an error status such as 400, 401, 404 or 409.</summary>
public class ApiException(HttpStatusCode status, string title, Dictionary<string, string[]>? errors)
    : Exception($"{(int)status} {title}")
{
    public HttpStatusCode Status { get; } = status;

    /// <summary>Per-field messages, present on validation errors (400).</summary>
    public Dictionary<string, string[]>? Errors { get; } = errors;
}
