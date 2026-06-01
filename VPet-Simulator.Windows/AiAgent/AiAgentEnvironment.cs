using System;

namespace VPet_Simulator.Windows.AiAgent;

internal static class AiAgentEnvironment
{
    public const string AiProvider = "VPET_AI_PROVIDER";
    public const string OllamaAutoStart = "VPET_OLLAMA_AUTO_START";
    public const string OllamaExePath = "VPET_OLLAMA_EXE";
    public const string OllamaUrl = "VPET_OLLAMA_URL";
    public const string OllamaModel = "VPET_OLLAMA_MODEL";
    public const string OpenAiApiKey = "OPENAI_API_KEY";
    public const string OpenAiModel = "VPET_OPENAI_MODEL";
    public const string GoogleClientId = "VPET_GOOGLE_CLIENT_ID";
    public const string GoogleClientSecret = "VPET_GOOGLE_CLIENT_SECRET";
    public const string GoogleRefreshToken = "VPET_GOOGLE_REFRESH_TOKEN";

    public static string Provider
    {
        get
        {
            var provider = Get(AiProvider);
            return string.IsNullOrWhiteSpace(provider) ? "ollama" : provider;
        }
    }

    public static bool IsOllamaAutoStartEnabled
    {
        get
        {
            var value = Get(OllamaAutoStart);
            return string.IsNullOrWhiteSpace(value) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string Get(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? "";
    }

    public static void SetUser(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, string.IsNullOrWhiteSpace(value) ? null : value, EnvironmentVariableTarget.User);
    }
}
