using LConnect.AutoProfiler.Application.Services;
using LConnect.AutoProfiler.Core.Interfaces;
using LConnect.AutoProfiler.Infrastructure.FileSystem;
using LConnect.AutoProfiler.Infrastructure.Http;
using LConnect.AutoProfiler.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "LConnect AutoProfiler";
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // ── Options (binding appsettings.json) ──────────────────────────────
        services.Configure<ProfileRuleOptions>(
            config.GetSection(ProfileRuleOptions.SectionName));

        services.Configure<ProfileParserOptions>(
            config.GetSection(ProfileParserOptions.SectionName));

        services.Configure<LConnectApiOptions>(
            config.GetSection(LConnectApiOptions.SectionName));

        // ── Infrastructure ───────────────────────────────────────────────────
        services.AddSingleton<IWindowMonitor, Win32WindowMonitor>();
        services.AddSingleton<IProfileParser, JsonProfileParser>();
        services.AddSingleton<IProfileRuleEngine, ProfileRuleEngine>();

        services.AddHttpClient<ILConnectApiClient, LocalLConnectClient>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        // ── Application (Orchestrateur = BackgroundService) ──────────────────
        services.AddHostedService<ProfileOrchestrator>();
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddEventLog(settings =>
        {
            settings.SourceName = "LConnect AutoProfiler";
        });
    })
    .Build();

await host.RunAsync();
