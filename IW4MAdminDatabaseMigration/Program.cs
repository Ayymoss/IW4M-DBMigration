using Microsoft.Extensions.DependencyInjection;

namespace IWDataMigration;

public static class Program
{
    public static async Task Main()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<AppEntry>();

        var app = serviceCollection.BuildServiceProvider();
        await app.GetRequiredService<AppEntry>().App();
    }
}
