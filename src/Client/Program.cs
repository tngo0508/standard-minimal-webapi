using System.Text;
using System.Text.Json;
using Client;
using Microsoft.Extensions.Configuration;

// A guided tour of the API. Run the API first, then run this program.
// Each step prints what it sends and what comes back.

// ---- Settings ---------------------------------------------------------------
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json")
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var baseUrl = config["Api:BaseUrl"]!;
var clientId = config["Api:ClientId"]!;
var clientSecret = config["Api:ClientSecret"];

if (string.IsNullOrEmpty(clientSecret))
{
    Console.WriteLine("The client secret is not set. Run step 2 of \"Run it for the first time\" in the README.");
    return 1;
}

using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
var api = new ApiClient(http, clientId, clientSecret);

try
{
    // ---- 1. No token --------------------------------------------------------
    Step(1, "Call a protected endpoint with no token");
    Sent("GET /products   (no Authorization header)");
    var anonymous = await http.GetAsync("/products");
    Got($"{(int)anonymous.StatusCode} {anonymous.StatusCode}   <- the API refuses: we have not proved who we are");

    // ---- 2. Log in ----------------------------------------------------------
    Step(2, "Log in to get a token");
    Sent($"POST /auth/login   {{ clientId: \"{clientId}\", clientSecret: \"********\" }}");
    var token = await api.LoginAsync();
    Got($"200 OK   token of {token.AccessToken.Length} characters, valid until {token.ExpiresAt.ToLocalTime():HH:mm:ss}");
    Note("A token is three parts separated by dots: header.payload.signature");
    Note("The payload is only encoded, not encrypted. Anyone can read it:");
    Console.WriteLine(Indent(DecodePayload(token.AccessToken)));
    Note("The signature is what stops anyone changing it. Only the API holds the signing key.");

    // ---- 3. Use the token ---------------------------------------------------
    Step(3, "Call a protected endpoint with the token");
    Sent("GET /auth/me   Authorization: Bearer <token>");
    var me = await api.GetMeAsync();
    Got($"200 OK   the API recognises us as \"{me.ClientId}\"");
    Note("From here on, ApiClient attaches the token to every request for us.");

    // ---- 4. Read ------------------------------------------------------------
    Step(4, "Read data");
    Sent("GET /categories");
    var categories = await api.GetCategoriesAsync();
    Got($"200 OK   {categories.Count} categories");
    foreach (var c in categories)
    {
        Console.WriteLine($"         {c.Id,3}  {c.Name}");
    }

    Sent("GET /products");
    var products = await api.GetProductsAsync();
    Got($"200 OK   {products.Count} products");
    foreach (var p in products)
    {
        Console.WriteLine($"         {p.Id,3}  {p.Name,-18} {p.Price,8:0.00}  {p.CategoryName ?? "(no category)"}");
    }

    var firstCategory = categories[0];
    Sent($"GET /products?categoryId={firstCategory.Id}");
    var filtered = await api.GetProductsAsync(firstCategory.Id);
    Got($"200 OK   {filtered.Count} products in \"{firstCategory.Name}\"");

    // ---- 5. Create ----------------------------------------------------------
    Step(5, "Create a category and a product in it");
    var categoryName = $"Demo {DateTime.Now:HHmmss}";
    Sent($"POST /categories   {{ name: \"{categoryName}\" }}");
    var category = await api.CreateCategoryAsync(categoryName);
    Got($"201 Created   new category has id {category.Id}");

    Sent($"POST /products   {{ name: \"Demo product\", price: 9.99, categoryId: {category.Id} }}");
    var product = await api.CreateProductAsync(new ProductRequest("Demo product", 9.99m, category.Id));
    Got($"201 Created   new product has id {product.Id}, category \"{product.CategoryName}\"");

    // ---- 6. Update ----------------------------------------------------------
    Step(6, "Update the product");
    Sent($"PUT /products/{product.Id}   {{ name: \"Demo product (renamed)\", price: 12.50, categoryId: {category.Id} }}");
    product = await api.UpdateProductAsync(product.Id, new ProductRequest("Demo product (renamed)", 12.50m, category.Id));
    Got($"200 OK   now \"{product.Name}\" at {product.Price:0.00}");

    // ---- 7. Errors ----------------------------------------------------------
    Step(7, "See what errors look like");

    Sent("POST /products   { name: \"\", price: -1 }   (breaks two validation rules)");
    await ExpectFailure(() => api.CreateProductAsync(new ProductRequest("", -1m)));

    Sent($"POST /categories   {{ name: \"{categoryName}\" }}   (name already exists)");
    await ExpectFailure(() => api.CreateCategoryAsync(categoryName));

    Sent("GET /products/999999   (no such product)");
    await ExpectFailure(() => api.GetProductAsync(999999));

    // ---- 8. Delete ----------------------------------------------------------
    Step(8, "Delete what we created, leaving the database as we found it");
    Sent($"DELETE /products/{product.Id}");
    await api.DeleteProductAsync(product.Id);
    Got("204 No Content");

    Sent($"DELETE /categories/{category.Id}");
    await api.DeleteCategoryAsync(category.Id);
    Got("204 No Content");

    Console.WriteLine();
    Console.WriteLine("Done. The whole workflow was: log in once, then send the token with every request.");
    return 0;
}
catch (HttpRequestException)
{
    Console.WriteLine();
    Console.WriteLine($"Could not reach the API at {baseUrl}. Start it first, in another window:");
    Console.WriteLine("  dotnet run --project src/Api --launch-profile http");
    return 1;
}
catch (ApiException ex)
{
    Console.WriteLine();
    Console.WriteLine($"The API returned an unexpected error: {ex.Message}");
    if (ex.Status == System.Net.HttpStatusCode.Unauthorized)
    {
        Console.WriteLine("Check that the client secret matches the API's. See step 2 of the first-run steps in the README.");
    }

    return 1;
}

// ---- Helpers for printing ---------------------------------------------------

static void Step(int number, string title)
{
    Console.WriteLine();
    Console.WriteLine($"--- Step {number}: {title} ---");
}

static void Sent(string text) => Console.WriteLine($"  -> {text}");

static void Got(string text) => Console.WriteLine($"  <- {text}");

static void Note(string text) => Console.WriteLine($"     {text}");

static string Indent(string text) =>
    string.Join(Environment.NewLine, text.Split('\n').Select(line => "       " + line.TrimEnd('\r')));

static async Task ExpectFailure(Func<Task> call)
{
    try
    {
        await call();
        Got("unexpectedly succeeded");
    }
    catch (ApiException ex)
    {
        Got(ex.Message);
        foreach (var (field, messages) in ex.Errors ?? [])
        {
            Console.WriteLine($"         {field}: {string.Join(" ", messages)}");
        }
    }
}

/// <summary>Reads the middle part of a JWT. No key is needed, which is exactly the point.</summary>
static string DecodePayload(string jwt)
{
    var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
    payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

    var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
    using var document = JsonDocument.Parse(json);
    return JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true });
}
