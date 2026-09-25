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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Collection>().HasKey(c => c.Name);

        modelBuilder.Entity<Document>()
            .HasOne<Collection>()
            .WithMany(c => c.Documents)
            .HasForeignKey(d => d.Collection)
            .OnDelete(DeleteBehavior.Cascade);

        // Re-ingesting the same file or URL into a collection refreshes its row instead of adding another.
        modelBuilder.Entity<Document>()
            .HasIndex(d => new { d.Collection, d.Source })
            .IsUnique();

        modelBuilder.Entity<Document>()
            .HasMany(d => d.Tags)
            .WithOne()
            .HasForeignKey(t => t.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DocumentTag>().HasKey(t => new { t.DocumentId, t.Name });
        modelBuilder.Entity<DocumentTag>().HasIndex(t => t.Name);

        modelBuilder.Entity<AppSettings>(settings =>
        {
            settings.Property(s => s.Id).ValueGeneratedNever();
            settings.ComplexProperty(s => s.Embeddings);
            settings.ComplexProperty(s => s.Vision);
            settings.ComplexProperty(s => s.Assistant);
            settings.ComplexProperty(s => s.Access);
        });

        modelBuilder.Entity<Invitation>().HasIndex(i => i.TokenHash).IsUnique();
        modelBuilder.Entity<Invitation>().HasIndex(i => i.Email);

        // A removed user's keys stop working with them.
        modelBuilder.Entity<ApiKey>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ApiKey>().HasIndex(k => k.KeyHash).IsUnique();
    }
}
