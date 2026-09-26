using Lantrn.Infra;
using Lantrn.Services;
using Lantrn.Services.Accounts;
using Lantrn.Services.Ingestion;
using Lantrn.Services.Search;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;

namespace Lantrn.Startup;

public static class CoreServices
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddSingleton<DocumentExtractor>();
        services.AddHttpClient<WebCrawler>(client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(WebCrawler.UserAgent);
                client.Timeout = TimeSpan.FromSeconds(30);
                client.MaxResponseContentBufferSize = WebCrawler.MaxPageBytes;
            })
            .ConfigurePrimaryHttpMessageHandler(WebCrawler.CreateHandler);
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<VisionOcrService>();
        services.AddSingleton<SearchAssistant>();
        services.AddSingleton<ModelCatalog>();
        services.AddSingleton(_ => new QdrantClient(
            configuration["Qdrant:Host"] ?? "localhost",
            configuration.GetValue("Qdrant:Port", 6334),
            configuration.GetValue("Qdrant:Https", false),
            configuration["Qdrant:ApiKey"]));
        services.AddSingleton<QdrantStore>();
        // A factory rather than a scoped context: a Blazor Server circuit lives far longer than one unit of work.
        services.AddDbContextFactory<DatabaseContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("Lantrn") ?? "Data Source=lantrn.db"));
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<DocumentStore>();
        services.AddSingleton<DocumentIngestor>();
        services.AddHostedService<DocumentFolderWatcher>();
        services.AddSingleton<AccountService>();

        // Keys live beside the data so sign-in cookies survive a container being recreated.
        var dataPath = Path.GetFullPath(
            configuration[$"{StorageOptions.SectionName}:DataPath"] ?? "data", environment.ContentRootPath);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));

        return services;
    }

    public static async Task InitializeAsync(this WebApplication app)
    {
        using (var db = app.Services.GetRequiredService<IDbContextFactory<DatabaseContext>>().CreateDbContext())
        {
            db.Database.Migrate();
        }

        await app.Services.GetRequiredService<SettingsStore>().LoadAsync();
        await app.Services.GetRequiredService<DocumentStore>().EnsureDefaultCollectionAsync();
        await app.Services.GetRequiredService<AccountService>().EnsureAdminAsync();
    }
}
