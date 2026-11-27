using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyFashion.Infrastructure.Data;

namespace MyFashion.Tests.Infrastructure;

/// <summary>An in-memory SQLite database with the real migrations applied. Lives as long as the instance.</summary>
public sealed class SqliteTestDatabase : IDbContextFactory<MyFashionDbContext>, IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly DbContextOptions<MyFashionDbContext> _options;

    public SqliteTestDatabase()
    {
        _connection.Open();
        _options = new DbContextOptionsBuilder<MyFashionDbContext>().UseSqlite(_connection).Options;
        using var db = CreateDbContext();
        db.Database.Migrate();
    }

    public MyFashionDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
