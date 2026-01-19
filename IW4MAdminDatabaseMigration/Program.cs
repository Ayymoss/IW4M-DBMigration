using IWDataMigration;
using IWDataMigration.Abstractions;
using IWDataMigration.Models;
using IWDataMigration.Services;
using IWDataMigration.UI;
using Microsoft.Extensions.DependencyInjection;

// Build service collection with all dependencies
var services = new ServiceCollection();

// Options
services.Configure<MigrationOptions>(_ => { });

// UI Services
services.AddSingleton<IConsoleService, ConsoleService>();

// Services
services.AddSingleton<IDataTransformer, DataTransformer>();
services.AddSingleton<TableDependencyResolver>();
services.AddSingleton<IConfigurationService, ConfigurationService>();

// Orchestrator
services.AddSingleton<MigrationOrchestrator>();

// Build and run
var serviceProvider = services.BuildServiceProvider();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var orchestrator = serviceProvider.GetRequiredService<MigrationOrchestrator>();
await orchestrator.RunAsync(cts.Token);
