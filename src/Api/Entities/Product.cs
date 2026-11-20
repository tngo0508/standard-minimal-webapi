namespace Api.Entities;

public class Product
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Optional: a product may be uncategorised.
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
}
