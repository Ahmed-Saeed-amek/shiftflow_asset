namespace ShiftFlow.Application.AI;

public class OpenAIOptions
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o";
}

public class AzureOpenAIOptions
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DeploymentName { get; set; } = "gpt-4o";
}

public class AzureSpeechOptions
{
    public string Key { get; set; } = "";
    public string Region { get; set; } = "eastus";
}

public class AiAssistantOptions
{
    public string DefaultVoice { get; set; } = "en-US-JennyNeural";
    public string SystemPrompt { get; set; } =
        "You are an AI assistant embedded in ShiftFlow, a shift management system. " +
        "Answer only from tool results. Be concise and professional.";
}
