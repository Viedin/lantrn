using Lantrn.Api;
using Lantrn.Components;
using Lantrn.Infra;
using Lantrn.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSingleton<DocumentExtractor>();
builder.Services.AddHttpClient<WebCrawler>(client =>
    {
        client.DefaultRequestHeaders.UserAgent.ParseAdd(WebCrawler.UserAgent);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.MaxResponseContentBufferSize = WebCrawler.MaxPageBytes;
    })
    .ConfigurePrimaryHttpMessageHandler(WebCrawler.CreateHandler);
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<VisionOcrService>();
builder.Services.AddSingleton<SearchAssistant>();
builder.Services.AddSingleton<ModelCatalog>();
builder.Services.AddSingleton(_ => new QdrantClient(
    builder.Configuration["Qdrant:Host"] ?? "localhost",
    builder.Configuration.GetValue("Qdrant:Port", 6334),
    builder.Configuration.GetValue("Qdrant:Https", false),
    builder.Configuration["Qdrant:ApiKey"]));
builder.Services.AddSingleton<QdrantStore>();
// A factory rather than a scoped context: a Blazor Server circuit lives far longer than one unit of work.
builder.Services.AddDbContextFactory<DatabaseContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Lantrn") ?? "Data Source=lantrn.db"));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton<DocumentStore>();
builder.Services.AddSingleton<DocumentIngestor>();
builder.Services.AddHostedService<DocumentFolderWatcher>();

// Keys live beside the data so sign-in cookies survive a container being recreated.
var dataPath = Path.GetFullPath(
    builder.Configuration[$"{StorageOptions.SectionName}:DataPath"] ?? "data", builder.Environment.ContentRootPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));

builder.Services.AddSingleton<AccountService>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/account/access-denied";
});
// Cookies pick up role changes and removed users as quickly as open circuits do.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = RevalidatingAuthenticationStateProvider.Interval);
builder.Services.AddSingleton<IAuthorizationHandler, SearchAccess>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(SearchAccess.Policy, policy => policy.AddRequirements(new SearchAccess.Requirement()));
builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<DatabaseContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddPublicApi();

var app = builder.Build();

using (var db = app.Services.GetRequiredService<IDbContextFactory<DatabaseContext>>().CreateDbContext())
{
    db.Database.Migrate();
}

await app.Services.GetRequiredService<SettingsStore>().LoadAsync();
await app.Services.GetRequiredService<DocumentStore>().EnsureDefaultCollectionAsync();
await app.Services.GetRequiredService<AccountService>().EnsureAdminAsync();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
// API clients get problem details for errors and bare status codes, never the site's HTML pages.
app.UseWhen(PublicApi.IsApiRequest, api =>
{
    api.UseExceptionHandler();
    api.UseStatusCodePages();
});
app.UseWhen(context => !PublicApi.IsApiRequest(context), site =>
    site.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
// Explicit so the re-executed /not-found request is routed and authorized again; the implicit
// middleware sits before the status code pages and never sees it.
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/account/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    // Search is the home page; when it isn't public it sends the visitor on to sign in.
    return Results.LocalRedirect("~/");
}).WithMetadata(new RequireAntiforgeryTokenAttribute());

app.MapGet("/documents/{id:guid}/original", async (Guid id, DocumentStore documents, CancellationToken cancellationToken) =>
    await documents.GetOriginalAsync(id, cancellationToken) is { } original
        ? Results.File(original.Path, original.ContentType, enableRangeProcessing: true)
        : Results.NotFound())
    .RequireAuthorization(SearchAccess.Policy);

app.MapPublicApi();

app.Run();
