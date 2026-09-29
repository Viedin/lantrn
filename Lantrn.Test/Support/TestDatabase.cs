using Lantrn.Infra;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Lantrn.Test.Support;

// A real SQLite schema in memory, so queries are translated the same way as in the app. It lives as long as the connection.
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<DatabaseContext> options;

    public TestDatabase()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<DatabaseContext>().UseSqlite(connection).Options;

        using (var db = CreateContext())
        {
            db.Database.EnsureCreated();
        }

        Factory = Substitute.For<IDbContextFactory<DatabaseContext>>();
        Factory.CreateDbContext().Returns(_ => CreateContext());
        Factory.CreateDbContextAsync(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(CreateContext()));
    }

    public IDbContextFactory<DatabaseContext> Factory { get; }

    public DatabaseContext CreateContext() => new(options);

    public void Dispose() => connection.Dispose();
}
