using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Client;

/// <summary>
/// One method per API endpoint. Callers never deal with tokens, headers or JSON:
/// this class logs in when needed and attaches the token to every request.
/// </summary>
public sealed class ApiClient(HttpClient http, string clientId, string clientSecret)
{
    private TokenResponse? _token;

    // ---- Auth -----------------------------------------------------------

    /// <summary>Exchanges the client id and secret for a token and remembers it.</summary>
    public async Task<TokenResponse> LoginAsync()
    {
        var response = await http.PostAsJsonAsync("/auth/login", new LoginRequest(clientId, clientSecret));
        await ThrowIfFailedAsync(response);

        _token = await ReadAsync<TokenResponse>(response);
        return _token;
    }

    public Task<ClientInfo> GetMeAsync() => GetAsync<ClientInfo>("/auth/me");

    // ---- Categories -----------------------------------------------------

    public Task<List<Category>> GetCategoriesAsync() => GetAsync<List<Category>>("/categories");

    public Task<Category> GetCategoryAsync(int id) => GetAsync<Category>($"/categories/{id}");

    public Task<Category> CreateCategoryAsync(string name) =>
        SendAsync<Category>(HttpMethod.Post, "/categories", new CategoryRequest(name));

    public Task<Category> RenameCategoryAsync(int id, string name) =>
        SendAsync<Category>(HttpMethod.Put, $"/categories/{id}", new CategoryRequest(name));

    public Task DeleteCategoryAsync(int id) => DeleteAsync($"/categories/{id}");

    // ---- Products -------------------------------------------------------

    public Task<List<Product>> GetProductsAsync(int? categoryId = null) =>
        GetAsync<List<Product>>(categoryId is null ? "/products" : $"/products?categoryId={categoryId}");

    public Task<Product> GetProductAsync(int id) => GetAsync<Product>($"/products/{id}");

    public Task<Product> CreateProductAsync(ProductRequest product) =>
        SendAsync<Product>(HttpMethod.Post, "/products", product);

    public Task<Product> UpdateProductAsync(int id, ProductRequest product) =>
        SendAsync<Product>(HttpMethod.Put, $"/products/{id}", product);

    public Task DeleteProductAsync(int id) => DeleteAsync($"/products/{id}");

    // ---- Plumbing -------------------------------------------------------

    private Task<T> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path);

    private async Task DeleteAsync(string path)
    {
        using var response = await SendAsync(HttpMethod.Delete, path);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null)
    {
        using var response = await SendAsync(method, path, body);
        return await ReadAsync<T>(response);
    }

    /// <summary>The one place every protected request goes through.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null)
    {
        // Log in on first use, and again shortly before the token runs out,
        // so the caller never sees an expired-token error.
        if (_token is null || _token.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
        {
            await LoginAsync();
        }

        using var request = new HttpRequestMessage(method, path);

        // This header is the whole of "being logged in": Authorization: Bearer <token>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token!.AccessToken);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        var response = await http.SendAsync(request);
        await ThrowIfFailedAsync(response);
        return response;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<T>()
        ?? throw new InvalidOperationException("The API returned an empty body.");

    /// <summary>Turns any non-success response into an <see cref="ApiException"/> carrying the API's own message.</summary>
    private static async Task ThrowIfFailedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        Problem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<Problem>();
        }
        catch (Exception)
        {
            // Not every failure has a JSON body; the status code alone is still useful.
        }

        throw new ApiException(response.StatusCode, problem?.Title ?? response.ReasonPhrase ?? "Request failed", problem?.Errors);
    }
}

public sealed class ApiException(HttpStatusCode status, string title, Dictionary<string, string[]>? errors)
    : Exception($"{(int)status} {title}")
{
    public HttpStatusCode Status { get; } = status;
    public Dictionary<string, string[]>? Errors { get; } = errors;
}
