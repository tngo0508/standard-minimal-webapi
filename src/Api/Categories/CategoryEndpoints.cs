using System.ComponentModel.DataAnnotations;
using Api.Data;
using Api.Entities;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Api.Categories;

public record CategoryRequest([property: Required, MaxLength(100)] string Name);

public record CategoryResponse(int Id, string Name);

public static class CategoryEndpoints
{
    public static IEndpointRouteBuilder MapCategoryEndpoints(this IEndpointRouteBuilder app)
    {
        // Every endpoint in this group requires a valid bearer token.
        var group = app.MapGroup("/categories").WithTags("Categories").RequireAuthorization();

        group.MapGet("/", GetAll);
        group.MapGet("/{id:int}", GetById).WithName("GetCategoryById");
        group.MapPost("/", Create).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/{id:int}", Update).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapDelete("/{id:int}", Delete);

        return app;
    }

    private static async Task<Ok<List<CategoryResponse>>> GetAll(AppDbContext db, CancellationToken ct)
    {
        var categories = await db.Categories
            .OrderBy(c => c.Name)
            .Select(c => new CategoryResponse(c.Id, c.Name))
            .ToListAsync(ct);

        return TypedResults.Ok(categories);
    }

    private static async Task<Results<Ok<CategoryResponse>, NotFound>> GetById(
        int id, AppDbContext db, CancellationToken ct)
    {
        var category = await db.Categories
            .Where(c => c.Id == id)
            .Select(c => new CategoryResponse(c.Id, c.Name))
            .SingleOrDefaultAsync(ct);

        return category is null ? TypedResults.NotFound() : TypedResults.Ok(category);
    }

    private static async Task<Results<CreatedAtRoute<CategoryResponse>, ProblemHttpResult>> Create(
        CategoryRequest request, AppDbContext db, CancellationToken ct)
    {
        var name = request.Name.Trim();

        if (await db.Categories.AnyAsync(c => c.Name == name, ct))
        {
            return NameTaken();
        }

        var category = new Category { Name = name };
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);

        return TypedResults.CreatedAtRoute(
            new CategoryResponse(category.Id, category.Name), "GetCategoryById", new { id = category.Id });
    }

    private static async Task<Results<Ok<CategoryResponse>, NotFound, ProblemHttpResult>> Update(
        int id, CategoryRequest request, AppDbContext db, CancellationToken ct)
    {
        var category = await db.Categories.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (category is null)
        {
            return TypedResults.NotFound();
        }

        var name = request.Name.Trim();

        if (await db.Categories.AnyAsync(c => c.Name == name && c.Id != id, ct))
        {
            return NameTaken();
        }

        category.Name = name;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CategoryResponse(category.Id, category.Name));
    }

    private static async Task<Results<NoContent, NotFound>> Delete(int id, AppDbContext db, CancellationToken ct)
    {
        // The foreign key is ON DELETE SET NULL, so products in this category survive.
        var deleted = await db.Categories.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static ProblemHttpResult NameTaken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "A category with this name already exists.");
}
