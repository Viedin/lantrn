using Lantrn.Api;
using Lantrn.Components;
using Lantrn.Services;
using Lantrn.Services.Accounts;
using Lantrn.Startup;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCoreServices(builder.Configuration, builder.Environment);
builder.Services.AddSiteAuthentication();
builder.Services.AddPublicApi();

var app = builder.Build();

await app.InitializeAsync();

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

app.MapAccountEndpoints();

app.MapGet("/documents/{id:guid}/original", async (Guid id, DocumentStore documents, CancellationToken cancellationToken) =>
    await documents.GetOriginalAsync(id, cancellationToken) is { } original
        ? Results.File(original.Path, original.ContentType, enableRangeProcessing: true)
        : Results.NotFound())
    .RequireAuthorization(SearchAccess.Policy);

app.MapPublicApi();

app.Run();
