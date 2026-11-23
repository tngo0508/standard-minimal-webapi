using System.ComponentModel.DataAnnotations;
using Api.Data;
using Api.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Api.Products;

public record ProductRequest(
    [Required, MaxLength(200)] string Name,
    [Range(0, 1_000_000)] decimal Price,
    int? CategoryId = null);

public record ProductResponse(int Id, string Name, decimal Price, int? CategoryId, string? CategoryName);

/// <summary>One page of search results, plus the numbers a caller needs to ask for the other pages.</summary>
public record ProductSearchResponse(
    List<ProductResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public static class ProductEndpoints
{
    // The largest page a caller may ask for from /products/search.
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        // Every endpoint in this group requires a valid bearer token.
        var group = app.MapGroup("/products").WithTags("Products").RequireAuthorization();

        group.MapGet("/", GetAll);
        group.MapGet("/search", Search);
        group.MapGet("/{id:int}", GetById).WithName("GetProductById");
        group.MapPost("/", Create);
        group.MapPut("/{id:int}", Update);
        group.MapDelete("/{id:int}", Delete);

        return app;
    }

    /// <summary>Lists products. Add ?categoryId=1 to the URL to list one category only.</summary>
    private static async Task<Ok<List<ProductResponse>>> GetAll(
        AppDbContext db, CancellationToken ct, int? categoryId = null)
    {
        // Include(...) tells EF Core to load each product's category in the same query (a JOIN).
        var query = db.Products.Include(p => p.Category).AsQueryable();

        if (categoryId is not null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        var products = await query.OrderBy(p => p.Id).ToListAsync(ct);

        var response = products.Select(ToResponse).ToList();
        return TypedResults.Ok(response);
    }

    /// <summary>
    /// Finds products one page at a time.
    /// Example: /products/search?name=ham&amp;categoryId=1&amp;page=2&amp;pageSize=20
    /// Every part of the query string is optional.
    /// </summary>
    private static async Task<Results<Ok<ProductSearchResponse>, ValidationProblem>> Search(
        AppDbContext db,
        CancellationToken ct,
        string? name = null,
        int? categoryId = null,
        int page = 1,
        int pageSize = 20)
    {
        // 1. Check the paging numbers. Pages are counted from 1, the way people count them.
        var errors = new Dictionary<string, string[]>();

        if (page < 1)
        {
            errors["page"] = ["page must be 1 or more."];
        }

        // The upper limit stops one request from asking for the whole table.
        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        // 2. Build the filter. Nothing is sent to the database yet: each Where only adds to the query.
        var query = db.Products.Include(p => p.Category).AsQueryable();

        if (!string.IsNullOrWhiteSpace(name))
        {
            var text = name.Trim();

            // Contains becomes SQL "LIKE '%text%'". With SQL Server's default settings it ignores case,
            // so "ham" finds "Hammer".
            query = query.Where(p => p.Name.Contains(text));
        }

        if (categoryId is not null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        // 3. Count every match, so the caller knows how many pages there are.
        var totalCount = await query.CountAsync(ct);

        // 4. Load only the rows of the requested page.
        //    Page 1 skips 0 rows, page 2 skips one page of rows, and so on.
        //    OrderBy is required: without a fixed order, the database may return rows in a different
        //    order each time, and a row could show up on two pages or on none.
        var rowsToSkip = (page - 1) * pageSize;

        var products = await query
            .OrderBy(p => p.Id)
            .Skip(rowsToSkip)
            .Take(pageSize)
            .ToListAsync(ct);

        // 5. Work out the number of pages, rounding up: 43 matches at 20 per page is 3 pages.
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = products.Select(ToResponse).ToList();
        var response = new ProductSearchResponse(items, page, pageSize, totalCount, totalPages);

        return TypedResults.Ok(response);
    }

    private static async Task<Results<Ok<ProductResponse>, NotFound>> GetById(
        int id, AppDbContext db, CancellationToken ct)
    {
        var product = await FindProductAsync(id, db, ct);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(ToResponse(product));
    }

    private static async Task<Results<CreatedAtRoute<ProductResponse>, ValidationProblem>> Create(
        ProductRequest request, AppDbContext db, CancellationToken ct)
    {
        if (!await CategoryExistsAsync(request.CategoryId, db, ct))
        {
            return UnknownCategoryProblem(request.CategoryId);
        }

        var product = new Product
        {
            Name = request.Name.Trim(),
            Price = request.Price,
            CategoryId = request.CategoryId,
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        // Read it back so the response includes the category name.
        var saved = await FindProductAsync(product.Id, db, ct);

        // 201 Created, with a Location header pointing at GET /products/{id}.
        return TypedResults.CreatedAtRoute(ToResponse(saved!), "GetProductById", new { id = product.Id });
    }

    private static async Task<Results<Ok<ProductResponse>, NotFound, ValidationProblem>> Update(
        int id, ProductRequest request, AppDbContext db, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        if (!await CategoryExistsAsync(request.CategoryId, db, ct))
        {
            return UnknownCategoryProblem(request.CategoryId);
        }

        product.Name = request.Name.Trim();
        product.Price = request.Price;
        product.CategoryId = request.CategoryId;
        await db.SaveChangesAsync(ct);

        // Read it back so the response includes the category name.
        var saved = await FindProductAsync(id, db, ct);

        return TypedResults.Ok(ToResponse(saved!));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, AppDbContext db, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([id], ct);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }

    // ---- Helpers --------------------------------------------------------

    /// <summary>Loads one product together with its category, or null if there is none.</summary>
    private static async Task<Product?> FindProductAsync(int id, AppDbContext db, CancellationToken ct)
    {
        return await db.Products
            .Include(p => p.Category)
            .SingleOrDefaultAsync(p => p.Id == id, ct);
    }

    /// <summary>A product may have no category. If it names one, that category must exist.</summary>
    private static async Task<bool> CategoryExistsAsync(int? categoryId, AppDbContext db, CancellationToken ct)
    {
        if (categoryId is null)
        {
            return true;
        }

        return await db.Categories.AnyAsync(c => c.Id == categoryId, ct);
    }

    /// <summary>A 400 response in the same shape as the automatic validation errors.</summary>
    private static ValidationProblem UnknownCategoryProblem(int? categoryId)
    {
        var errors = new Dictionary<string, string[]>
        {
            ["CategoryId"] = [$"Category {categoryId} does not exist."],
        };

        return TypedResults.ValidationProblem(errors);
    }

    private static ProductResponse ToResponse(Product product)
    {
        return new ProductResponse(
            product.Id,
            product.Name,
            product.Price,
            product.CategoryId,
            product.Category?.Name);
    }
}
