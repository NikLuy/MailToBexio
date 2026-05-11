namespace MailToBexio.Configuration;

public class GraphSettings
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TargetMailboxUpn { get; set; } = string.Empty;
    public string MailFolderName { get; set; } = "Kunden_Erfassung";
    public string ErrorFolderName { get; set; } = "Fehler";
}

public class BexioSettings
{
    public string BaseUrl { get; set; } = "https://api.bexio.com/2.0";
    public string ApiKey { get; set; } = string.Empty;
}

public class AiSettings
{
    public string Provider { get; set; } = "Copilot";

    // Azure OpenAI / Copilot
    public string CopilotEndpoint { get; set; } = string.Empty;
    public string CopilotApiKey { get; set; } = string.Empty;
    public string CopilotDeploymentName { get; set; } = "gpt-4o";

    // Gemini
    public string GeminiApiKey { get; set; } = string.Empty;

    // Ollama (lokal)
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3";
}

public class WorkerSettings
{
    public int IntervalMinutes { get; set; } = 5;
}
