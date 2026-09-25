using Lantrn.Infra;
using Microsoft.EntityFrameworkCore;

namespace Lantrn.Services;

// Holds the settings row in memory, so services can read it synchronously on every call and pick up a save at once.
public sealed class SettingsStore(
    IDbContextFactory<DatabaseContext> dbFactory,
    IConfiguration configuration,
    ILogger<SettingsStore> logger)
{
    private volatile AppSettings? current;

    public AppSettings Current => current ?? throw new InvalidOperationException("Settings are loaded at startup, before any request.");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.Settings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

        if (settings is null)
        {
            settings = CreateDefaults();
            db.Settings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Created the settings row: embeddings '{EmbeddingModel}' at {EmbeddingUrl}, vision '{VisionModel}' at {VisionUrl}",
                settings.Embeddings.Model, settings.Embeddings.BaseUrl, settings.Vision.Model, settings.Vision.BaseUrl);
        }

        current = settings;
    }

    // What a fresh install starts with: the built-in values, overlaid by any Embeddings/Vision/Assistant sections in
    // configuration, so a deployment can pre-set its endpoints.
    public AppSettings CreateDefaults()
    {
        var settings = new AppSettings();
        configuration.GetSection("Embeddings").Bind(settings.Embeddings);
        configuration.GetSection("Vision").Bind(settings.Vision);
        configuration.GetSection("Assistant").Bind(settings.Assistant);
        configuration.GetSection("Access").Bind(settings.Access);
        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var saved = settings.Copy();
        saved.Id = AppSettings.SingletonId;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.Settings.Update(saved);
        await db.SaveChangesAsync(cancellationToken);

        current = saved;
        logger.LogInformation("Saved settings: embeddings '{EmbeddingModel}' at {EmbeddingUrl}, vision '{VisionModel}' at {VisionUrl}",
            saved.Embeddings.Model, saved.Embeddings.BaseUrl, saved.Vision.Model, saved.Vision.BaseUrl);
    }
}
