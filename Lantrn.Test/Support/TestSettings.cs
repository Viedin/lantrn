using Lantrn.Infra;
using Lantrn.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lantrn.Test.Support;

public static class TestSettings
{
    public static async Task<SettingsStore> LoadAsync(TestDatabase database, Action<AppSettings> configure)
    {
        await using (var db = database.CreateContext())
        {
            var settings = new AppSettings();
            configure(settings);
            db.Settings.Add(settings);
            await db.SaveChangesAsync();
        }

        var store = new SettingsStore(database.Factory, new ConfigurationBuilder().Build(), NullLogger<SettingsStore>.Instance);
        await store.LoadAsync();
        return store;
    }
}
