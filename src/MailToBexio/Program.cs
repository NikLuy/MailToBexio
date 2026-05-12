using MailToBexio.Configuration;
using MailToBexio.Services;
using MailToBexio.Services.AI;
using MailToBexio.Workers;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, config) => config
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/mailtobexio-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30));

    builder.Services.Configure<GraphSettings>(builder.Configuration.GetSection("Graph"));
    builder.Services.Configure<BexioSettings>(builder.Configuration.GetSection("Bexio"));
    builder.Services.Configure<AiSettings>(builder.Configuration.GetSection("AI"));
    builder.Services.Configure<WorkerSettings>(builder.Configuration.GetSection("Worker"));

    var bexioSettings = builder.Configuration.GetSection("Bexio").Get<BexioSettings>()!;
    builder.Services.AddHttpClient("Bexio", client =>
    {
        var baseUrl = bexioSettings.BaseUrl.EndsWith('/')
            ? bexioSettings.BaseUrl
            : $"{bexioSettings.BaseUrl}/";
        client.BaseAddress = new Uri(baseUrl);
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bexioSettings.ApiKey);
        client.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
    });

    var aiSettings = builder.Configuration.GetSection("AI").Get<AiSettings>()!;
    builder.Services.AddHttpClient("Gemini");
    builder.Services.AddHttpClient("Ollama", client =>
    {
        client.BaseAddress = new Uri(aiSettings.OllamaBaseUrl);
    });

    builder.Services.AddSingleton<IGraphMailService, GraphMailService>();
    builder.Services.AddSingleton<IBexioService, BexioService>();
    builder.Services.AddSingleton<CopilotService>();
    builder.Services.AddSingleton<GeminiService>();
    builder.Services.AddSingleton<OllamaService>();

    // AI-Provider dynamisch wählen basierend auf Konfiguration (Default: Copilot)
    builder.Services.AddSingleton<IAIService>(sp =>
    {
        var provider = aiSettings.Provider;
        return provider switch
        {
            "Gemini" => (IAIService)sp.GetRequiredService<GeminiService>(),
            "Ollama" => sp.GetRequiredService<OllamaService>(),
            _ => sp.GetRequiredService<CopilotService>()
        };
    });

    builder.Services.AddHostedService<MailProcessingWorker>();

    var app = builder.Build();
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host unerwartet beendet");
}
finally
{
    await Log.CloseAndFlushAsync();
}
