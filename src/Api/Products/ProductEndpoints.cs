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

public static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        // Every endpoint in this group requires a valid bearer token.
        var group = app.MapGroup("/products").WithTags("Products").RequireAuthorization();

        group.MapGet("/", GetAll);
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
