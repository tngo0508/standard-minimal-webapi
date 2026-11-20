using System.ComponentModel.DataAnnotations;
using Api.Data;
using Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Api.Products;

public record ProductRequest(
    [property: Required, MaxLength(200)] string Name,
    [property: Range(0, 1_000_000)] decimal Price);

public record ProductResponse(int Id, string Name, decimal Price, DateTimeOffset CreatedAt);

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

    private static async Task<IResult> GetAll(AppDbContext db, CancellationToken ct)
    {
        var products = await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Price, p.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(products);
    }

    private static async Task<IResult> GetById(int id, AppDbContext db, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? Results.NotFound() : Results.Ok(ToResponse(product));
    }

    private static async Task<IResult> Create(ProductRequest request, AppDbContext db, CancellationToken ct)
    {
        var product = new Product { Name = request.Name.Trim(), Price = request.Price };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return Results.CreatedAtRoute("GetProductById", new { id = product.Id }, ToResponse(product));
    }

    private static async Task<IResult> Update(int id, ProductRequest request, AppDbContext db, CancellationToken ct)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (product is null)
        {
            return Results.NotFound();
        }

        product.Name = request.Name.Trim();
        product.Price = request.Price;
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToResponse(product));
    }

    private static async Task<IResult> Delete(int id, AppDbContext db, CancellationToken ct)
    {
        var deleted = await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static ProductResponse ToResponse(Product p) => new(p.Id, p.Name, p.Price, p.CreatedAt);
}
