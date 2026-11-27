using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MyFashion.Infrastructure.Data;

/// <summary>
/// The application database. The model uses only portable types (enums as strings, UTC DateTime,
/// explicit lengths) so it can move to SQL Server or PostgreSQL by switching the provider.
/// </summary>
public sealed class MyFashionDbContext(DbContextOptions<MyFashionDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    public DbSet<RunEntity> Runs => Set<RunEntity>();

    public DbSet<MediaItemEntity> MediaItems => Set<MediaItemEntity>();

    public DbSet<GarmentEntity> Garments => Set<GarmentEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AccountEntity>(account =>
        {
            account.Property(a => a.Username).HasMaxLength(100);
            account.HasIndex(a => a.Username).IsUnique();
        });

        modelBuilder.Entity<RunEntity>(run =>
        {
            run.Property(r => r.Source).HasConversion<string>().HasMaxLength(20);
            run.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            run.Property(r => r.Model).HasMaxLength(100);
            run.Property(r => r.Error).HasMaxLength(1000);
            run.HasOne(r => r.Account).WithMany(a => a.Runs).HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Cascade);
            run.HasIndex(r => new { r.AccountId, r.StartedAtUtc });
        });

        modelBuilder.Entity<MediaItemEntity>(media =>
        {
            media.Property(m => m.Kind).HasConversion<string>().HasMaxLength(20);
            media.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
            media.Property(m => m.SourcePath).HasMaxLength(1000);
            media.Property(m => m.PreparedPath).HasMaxLength(1000);
            media.Property(m => m.Sha256).HasMaxLength(64);
            media.Property(m => m.Caption).HasMaxLength(2200);
            media.Property(m => m.Error).HasMaxLength(1000);
            media.Property(m => m.StyleTags).HasMaxLength(200);
            media.HasOne(m => m.Run).WithMany(r => r.MediaItems).HasForeignKey(m => m.RunId).OnDelete(DeleteBehavior.Cascade);
            media.HasIndex(m => m.Sha256);
        });

        modelBuilder.Entity<GarmentEntity>(garment =>
        {
            garment.Property(g => g.Category).HasConversion<string>().HasMaxLength(20);
            garment.Property(g => g.Pattern).HasConversion<string>().HasMaxLength(20);
            garment.Property(g => g.Fit).HasConversion<string>().HasMaxLength(20);
            garment.Property(g => g.BrandEvidence).HasConversion<string>().HasMaxLength(20);
            garment.Property(g => g.Type).HasMaxLength(60);
            garment.Property(g => g.Color).HasMaxLength(30);
            garment.Property(g => g.Brand).HasMaxLength(60);
            garment.HasOne(g => g.MediaItem).WithMany(m => m.Garments).HasForeignKey(g => g.MediaItemId).OnDelete(DeleteBehavior.Cascade);
            garment.HasIndex(g => g.Brand);
        });
    }
}

/// <summary>Lets <c>dotnet ef</c> create the context at design time (for migrations).</summary>
public sealed class MyFashionDbContextFactory : IDesignTimeDbContextFactory<MyFashionDbContext>
{
    public MyFashionDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MyFashionDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
