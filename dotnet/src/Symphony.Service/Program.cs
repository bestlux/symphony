using Symphony.Service.Cli;
using Symphony.Service.Hosting;
using Symphony.Service.Logging;
using Symphony.Service.Observability;
using Symphony.Abstractions.Tracking;
using Symphony.Core.Agents;
using Symphony.Core.Configuration;
using Symphony.Core.Prompts;
using Symphony.Core.Workflow;
using Symphony.Codex;
using Symphony.Linear;
using Symphony.Workspaces;
using Microsoft.Extensions.FileProviders;

var parsed = CliParser.Parse(args);
if (!parsed.Success)
{
    Console.Error.WriteLine(parsed.Error);
    return 1;
}

var loadedSecrets = SecretsLoader.Load(parsed.Options.SecretsPath);
if (!string.IsNullOrWhiteSpace(parsed.Options.SecretsPath))
{
    Console.WriteLine($"Loaded {loadedSecrets} secret value(s) from {parsed.Options.SecretsPath}");
}

if (parsed.Options.Port is { } port)
{
    var builder = WebApplication.CreateBuilder(args);
    ConfigureServices(builder.Services, parsed.Options);
    builder.Logging.AddProvider(new LogFileWriterProvider(parsed.Options.LogsRoot));
    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

    var app = builder.Build();
    var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    if (Directory.Exists(webRoot))
    {
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(webRoot) });
    }

    HttpApi.Map(app);
    await app.RunAsync();
    return 0;
}

var hostBuilder = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging => logging.AddProvider(new LogFileWriterProvider(parsed.Options.LogsRoot)))
    .ConfigureServices(services => ConfigureServices(services, parsed.Options));

await hostBuilder.RunConsoleAsync();
return 0;

static void ConfigureServices(IServiceCollection services, CliOptions options)
{
    services.AddSingleton(options);
    services.AddSingleton(new WorkflowStore(options.WorkflowPath));
    services.AddSingleton<ConfigResolver>();
    services.AddSingleton<PromptRenderer>();
    services.AddSingleton<ConfigBackedOptions>();
    services.AddSingleton<ILinearOptionsProvider>(sp => sp.GetRequiredService<ConfigBackedOptions>());
    services.AddSingleton<IWorkspaceOptionsProvider>(sp => sp.GetRequiredService<ConfigBackedOptions>());
    services.AddHttpClient("linear");
    services.AddSingleton(sp => new LinearClient(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("linear"),
        sp.GetRequiredService<ILinearOptionsProvider>()));
    services.AddSingleton<ITrackerClient, LinearTrackerClient>();
    services.AddSingleton<WorkspaceManager>();
    services.AddSingleton<IWorkspaceCoordinator>(sp => sp.GetRequiredService<WorkspaceManager>());
    services.AddSingleton<DynamicToolDispatcher>();
    services.AddSingleton<CodexAppServerClient>();
    services.AddSingleton<ICodexSessionClient>(sp => sp.GetRequiredService<CodexAppServerClient>());
    services.AddSingleton<IAgentRunner, AgentRunner>();
    services.AddSingleton<RuntimeStateStore>();
    services.AddSingleton<ManualRefreshSignal>();
    services.AddSingleton<DaemonControlService>();
    services.AddSingleton<MergeWorkflowService>();
    services.AddHostedService<SymphonyHostedService>();
    services.AddHostedService<ConsoleDashboard>();
}
