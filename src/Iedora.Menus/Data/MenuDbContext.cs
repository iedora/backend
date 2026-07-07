using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Iedora.Menus;

/// <summary>
/// The <b>Menu</b> module's store, under the <c>menu</c> Postgres schema: the content hierarchy
/// (restaurants → menus → categories → items). Structured values (i18n maps, theme, item variants)
/// are stored as <c>jsonb</c>; flat string lists (supported languages, tags) as Postgres <c>text[]</c>.
/// <see cref="Restaurant.TenantId"/> references an auth-service tenant by id — no cross-module FK.
/// </summary>
public sealed class MenuDbContext(DbContextOptions<MenuDbContext> options) : DbContext(options)
{
    public const string Schema = "menu";

    public DbSet<Restaurant> Restaurants => Set<Restaurant>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<QrCode> QrCodes => Set<QrCode>();

    // camelCase JSON so the jsonb keys match the Bun service's shape (labelI18n, priceCents, …).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<Restaurant>(restaurant =>
        {
            restaurant.ToTable("restaurants");
            restaurant.HasKey(r => r.Id);
            restaurant.Property(r => r.Id).HasDefaultValueSql("uuidv7()");
            restaurant.HasIndex(r => r.Slug).IsUnique();
            restaurant.HasIndex(r => r.TenantId);
            restaurant.Property(r => r.Name).IsRequired();
            restaurant.Property(r => r.Slug).IsRequired();
            restaurant.Property(r => r.DefaultLanguage).IsRequired().HasDefaultValue("en");
            restaurant.Property(r => r.DefaultCurrency).IsRequired().HasDefaultValue("EUR");
            restaurant.Property(r => r.SupportedLanguages).HasColumnType("text[]");
            restaurant.Property(r => r.DescriptionI18n).HasJsonb(Json);
            restaurant.Property(r => r.Theme).HasJsonb(Json);
            restaurant.Property(r => r.CreatedAt).HasDefaultValueSql("now()");
            restaurant.Property(r => r.UpdatedAt).HasDefaultValueSql("now()");
        });

        builder.Entity<Menu>(menu =>
        {
            menu.ToTable("menus");
            menu.HasKey(m => m.Id);
            menu.Property(m => m.Id).HasDefaultValueSql("uuidv7()");
            menu.HasIndex(m => m.RestaurantId);
            menu.Property(m => m.Name).IsRequired();
            menu.Property(m => m.Active).HasDefaultValue(true);
            menu.Property(m => m.NameI18n).HasJsonb(Json);
            menu.Property(m => m.DescriptionI18n).HasJsonb(Json);
            menu.Property(m => m.CreatedAt).HasDefaultValueSql("now()");
            menu.Property(m => m.UpdatedAt).HasDefaultValueSql("now()");
            menu.HasOne<Restaurant>().WithMany().HasForeignKey(m => m.RestaurantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Category>(category =>
        {
            category.ToTable("categories");
            category.HasKey(c => c.Id);
            category.Property(c => c.Id).HasDefaultValueSql("uuidv7()");
            category.HasIndex(c => c.MenuId);
            category.HasIndex(c => c.RestaurantId);
            category.Property(c => c.Name).IsRequired();
            category.Property(c => c.NameI18n).HasJsonb(Json);
            category.Property(c => c.DescriptionI18n).HasJsonb(Json);
            category.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
            category.Property(c => c.UpdatedAt).HasDefaultValueSql("now()");
            category.HasOne<Menu>().WithMany().HasForeignKey(c => c.MenuId).OnDelete(DeleteBehavior.Cascade);
            category.HasOne<Restaurant>().WithMany().HasForeignKey(c => c.RestaurantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Item>(item =>
        {
            item.ToTable("items");
            item.HasKey(i => i.Id);
            item.Property(i => i.Id).HasDefaultValueSql("uuidv7()");
            item.HasIndex(i => i.CategoryId);
            item.HasIndex(i => i.RestaurantId);
            item.Property(i => i.Name).IsRequired();
            item.Property(i => i.Currency).IsRequired().HasDefaultValue("EUR");
            item.Property(i => i.Available).HasDefaultValue(true);
            item.Property(i => i.Tags).HasColumnType("text[]");
            item.Property(i => i.NameI18n).HasJsonb(Json);
            item.Property(i => i.DescriptionI18n).HasJsonb(Json);
            item.Property(i => i.Variants).HasJsonb(Json);
            item.Property(i => i.CreatedAt).HasDefaultValueSql("now()");
            item.Property(i => i.UpdatedAt).HasDefaultValueSql("now()");
            item.HasOne<Category>().WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Cascade);
            item.HasOne<Restaurant>().WithMany().HasForeignKey(i => i.RestaurantId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<QrCode>(qr =>
        {
            qr.ToTable("qr_codes");
            qr.HasKey(q => q.Code);
            qr.HasIndex(q => q.RestaurantId);
            qr.Property(q => q.CreatedAt).HasDefaultValueSql("now()");
            // A sticker outlives the restaurant it was bound to (reusable): SET NULL, not cascade.
            qr.HasOne<Restaurant>().WithMany().HasForeignKey(q => q.RestaurantId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}

/// <summary>Maps a reference-typed property to a <c>jsonb</c> column via System.Text.Json, with a
/// value comparer (compare/snapshot by serialized form) so EF tracks mutations to the blob.</summary>
internal static class JsonbPropertyExtensions
{
    public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> HasJsonb<T>(
        this Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> property, JsonSerializerOptions json)
        where T : class?
    {
        var converter = new ValueConverter<T, string>(
            v => JsonSerializer.Serialize(v, json),
            v => JsonSerializer.Deserialize<T>(v, json)!);
        var comparer = new ValueComparer<T>(
            (a, b) => JsonSerializer.Serialize(a, json) == JsonSerializer.Serialize(b, json),
            v => v == null ? 0 : JsonSerializer.Serialize(v, json).GetHashCode(),
            v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, json), json)!);
        property.HasColumnType("jsonb").HasConversion(converter, comparer);
        return property;
    }
}
