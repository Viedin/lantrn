using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Lantrn.Infra;

public class DatabaseContext(DbContextOptions<DatabaseContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<AppSettings> Settings => Set<AppSettings>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<Source> Sources => Set<Source>();

    // Each entity's mapping lives beside it, in its IEntityTypeConfiguration.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DatabaseContext).Assembly);
    }
}
