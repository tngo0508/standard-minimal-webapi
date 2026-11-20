using System.ComponentModel.DataAnnotations;
using Api.Data;
using Api.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Api.Products;

public record ProductRequest(
    [property: Required, MaxLength(200)] string Name,
    [property: Range(0, 1_000_000)] decimal Price,
    int? CategoryId = null);

public record ProductResponse(
    int Id,
    string Name,
    decimal Price,
    DateTimeOffset CreatedAt,
    int? CategoryId,
    string? CategoryName);

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

    /// <summary>Lists products, optionally only those in one category.</summary>
    private static async Task<Ok<List<ProductResponse>>> GetAll(
        AppDbContext db, CancellationToken ct, int? categoryId = null)
    {
        var query = db.Products.AsNoTracking();

        if (categoryId is not null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }

        // Projecting through the navigation makes EF emit a LEFT JOIN to Categories.
        var products = await query
            .OrderBy(p => p.Id)
            .Select(p => new ProductResponse(
                p.Id, p.Name, p.Price, p.CreatedAt, p.CategoryId, p.Category != null ? p.Category.Name : null))
            .ToListAsync(ct);

        return TypedResults.Ok(products);
    }

    private static async Task<Results<Ok<ProductResponse>, NotFound>> GetById(
        int id, AppDbContext db, CancellationToken ct)
    {
        var product = await db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .SingleOrDefaultAsync(p => p.Id == id, ct);

        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ToResponse(product));
    }

    private static async Task<Results<CreatedAtRoute<ProductResponse>, ValidationProblem>> Create(
        ProductRequest request, AppDbContext db, CancellationToken ct)
    {
        var (category, problem) = await ResolveCategoryAsync(request.CategoryId, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        var product = new Product { Name = request.Name.Trim(), Price = request.Price, Category = category };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return TypedResults.CreatedAtRoute(ToResponse(product), "GetProductById", new { id = product.Id });
    }

    private static async Task<Results<Ok<ProductResponse>, NotFound, ValidationProblem>> Update(
        int id, ProductRequest request, AppDbContext db, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        var (category, problem) = await ResolveCategoryAsync(request.CategoryId, db, ct);
        if (problem is not null)
        {
            return problem;
        }

        product.Name = request.Name.Trim();
        product.Price = request.Price;
        product.CategoryId = category?.Id;
        product.Category = category;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(ToResponse(product));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, AppDbContext db, CancellationToken ct)
    {
        var deleted = await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    /// <summary>
    /// A CategoryId that points at nothing is a client error, reported the same way as
    /// the data-annotation failures rather than surfacing as a foreign key exception.
    /// </summary>
    private static async Task<(Category? Category, ValidationProblem? Problem)> ResolveCategoryAsync(
        int? categoryId, AppDbContext db, CancellationToken ct)
    {
        if (categoryId is null)
        {
            return (null, null);
        }

        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == categoryId, ct);
        if (category is not null)
        {
            return (category, null);
        }

        var errors = new Dictionary<string, string[]>
        {
            [nameof(ProductRequest.CategoryId)] = [$"Category {categoryId} does not exist."],
        };
        return (null, TypedResults.ValidationProblem(errors));
    }

    private static ProductResponse ToResponse(Product p) =>
        new(p.Id, p.Name, p.Price, p.CreatedAt, p.CategoryId, p.Category?.Name);
}
