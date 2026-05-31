using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class WebSearchClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<string> BuildWebSummaryIfNeededAsync(string userText, CancellationToken cancellationToken)
    {
        if (!AiAgentEnvironment.IsWebSearchEnabled || !ShouldSearch(userText))
            return "";

        return await SearchAsync(userText, cancellationToken);
    }

    private static async Task<string> SearchAsync(string query, CancellationToken cancellationToken)
    {
        var apiKey = AiAgentEnvironment.Get(AiAgentEnvironment.VertexSearchApiKey);
        var projectId = AiAgentEnvironment.Get(AiAgentEnvironment.VertexSearchProjectId);
        var appId = AiAgentEnvironment.Get(AiAgentEnvironment.VertexSearchAppId);
        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(projectId)
            || string.IsNullOrWhiteSpace(appId))
            return "\u7db2\u8def\u641c\u5c0b\u5c1a\u672a\u8a2d\u5b9a Vertex AI Search\u3002\u8acb\u5728 AI \u8a2d\u5b9a\u586b\u5165 Project ID\u3001App ID \u548c API Key\u3002";

        return await SearchVertexAiAsync(BuildSearchQuery(query), projectId, appId, apiKey, cancellationToken);
    }

    private static string BuildSearchQuery(string query)
    {
        var input = (query ?? "").Trim();
        if (string.IsNullOrWhiteSpace(input))
            return "";

        var lowerInput = input.ToLowerInvariant();
        var currentYear = DateTime.Now.Year;
        if (lowerInput.Contains("\u897f\u5340")
            && (lowerInput.Contains("\u51a0\u8ecd\u8cfd") || lowerInput.Contains("\u51a0\u8ecd") || lowerInput.Contains("\u6c7a\u8cfd") || lowerInput.Contains("\u5f97\u4e3b")))
            return $"{currentYear} NBA Western Conference Finals winner {input}";

        if (lowerInput.Contains("\u6771\u5340")
            && (lowerInput.Contains("\u51a0\u8ecd\u8cfd") || lowerInput.Contains("\u51a0\u8ecd") || lowerInput.Contains("\u6c7a\u8cfd") || lowerInput.Contains("\u5f97\u4e3b")))
            return $"{currentYear} NBA Eastern Conference Finals winner {input}";

        if ((lowerInput.Contains("nba") || lowerInput.Contains("\u7c43\u7403"))
            && (lowerInput.Contains("\u51a0\u8ecd") || lowerInput.Contains("\u5b63\u5f8c\u8cfd") || lowerInput.Contains("\u6bd4\u6578") || lowerInput.Contains("\u6230\u7e3e") || lowerInput.Contains("\u8ab0")))
            return $"{currentYear} NBA playoffs {input}";

        return input;
    }

    private static async Task<string> SearchVertexAiAsync(string query, string projectId, string appId, string apiKey, CancellationToken cancellationToken)
    {
        var location = AiAgentEnvironment.Get(AiAgentEnvironment.VertexSearchLocation);
        if (string.IsNullOrWhiteSpace(location))
            location = "global";

        var servingConfig = $"projects/{projectId}/locations/{location}/collections/default_collection/engines/{appId}/servingConfigs/default_search";
        var host = location.Equals("global", StringComparison.OrdinalIgnoreCase)
            ? "discoveryengine.googleapis.com"
            : location + "-discoveryengine.googleapis.com";
        var url = $"https://{host}/v1/{servingConfig}:searchLite?key={Uri.EscapeDataString(apiKey)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Method = HttpMethod.Post;
        request.Headers.UserAgent.ParseAdd("VPet-AI-Agent/1.0");
        request.Headers.Accept.ParseAdd("application/json");
        request.Content = new StringContent(JsonSerializer.Serialize(new
        {
            servingConfig,
            query,
            pageSize = 5,
            userPseudoId = "vpet-desktop"
        }), Encoding.UTF8, "application/json");

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return $"Vertex AI Search \u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}";

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
                return "\u7db2\u8def\u641c\u5c0b\u6c92\u6709\u627e\u5230\u7d50\u679c\u3002";

            var builder = new StringBuilder();
            builder.AppendLine("Vertex AI Search \u7d50\u679c\uff1a");
            var count = 0;
            foreach (var result in results.EnumerateArray().Take(5))
            {
                var source = GetResultSource(result);
                var title = Clean(GetFirstJsonString(source, "title", "htmlTitle", "name"));
                var link = Clean(GetFirstJsonString(source, "link", "url", "uri", "formattedUrl"));
                var description = Clean(GetDescription(source));
                if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
                    continue;

                builder.AppendLine("- " + title);
                if (!string.IsNullOrWhiteSpace(description))
                    builder.AppendLine("  " + description);
                if (!string.IsNullOrWhiteSpace(link))
                    builder.AppendLine("  " + link);
                count++;
            }

            return count == 0
                ? "\u7db2\u8def\u641c\u5c0b\u6c92\u6709\u627e\u5230\u7d50\u679c\u3002"
                : builder.ToString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            return "Vertex AI Search \u5931\u6557\uff1a" + ex.Message;
        }
    }

    private static JsonElement GetResultSource(JsonElement result)
    {
        if (result.TryGetProperty("document", out var document))
        {
            if (document.TryGetProperty("derivedStructData", out var derivedStructData))
                return derivedStructData;
            if (document.TryGetProperty("structData", out var structData))
                return structData;
            return document;
        }

        return result;
    }

    private static string GetFirstJsonString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String)
                return property.GetString() ?? "";
        }
        return "";
    }

    private static string GetDescription(JsonElement element)
    {
        var description = GetFirstJsonString(element, "description", "snippet", "htmlSnippet");
        if (!string.IsNullOrWhiteSpace(description))
            return description;

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("snippets", out var snippets)
            && snippets.ValueKind == JsonValueKind.Array)
            return string.Join(" ", snippets.EnumerateArray()
                .Select(snippet => snippet.ValueKind == JsonValueKind.String
                    ? snippet.GetString() ?? ""
                    : GetFirstJsonString(snippet, "snippet", "htmlSnippet"))
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty("extractive_answers", out var extractiveAnswers)
            && extractiveAnswers.ValueKind == JsonValueKind.Array)
            return string.Join(" ", extractiveAnswers.EnumerateArray()
                .Select(answer => GetFirstJsonString(answer, "content"))
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        return "";
    }

    private static bool ShouldSearch(string text)
    {
        var input = (text ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var explicitKeywords = new[]
        {
            "\u641c\u5c0b",
            "\u641c\u5c0b\u4e00\u4e0b",
            "\u67e5\u4e00\u4e0b",
            "\u5e6b\u6211\u67e5",
            "\u4e0a\u7db2\u67e5",
            "\u7db2\u8def",
            "\u6700\u65b0",
            "\u65b0\u805e",
            "\u4eca\u5929\u65b0\u805e",
            "google",
            "web",
            "internet",
            "search"
        };
        if (explicitKeywords.Any(input.Contains))
            return true;

        var sportsResultKeywords = new[]
        {
            "\u51a0\u8ecd\u8cfd",
            "\u7e3d\u51a0\u8ecd",
            "\u51a0\u8ecd",
            "\u5f97\u4e3b",
            "\u52dd\u968a",
            "\u8ab0\u8d0f",
            "\u6bd4\u6578",
            "\u6230\u7e3e",
            "\u5b63\u5f8c\u8cfd",
            "\u897f\u5340",
            "\u6771\u5340",
            "nba",
            "mlb",
            "nfl",
            "nhl",
            "finals",
            "playoffs",
            "winner"
        };
        if (sportsResultKeywords.Any(input.Contains))
            return true;

        var currentInfoKeywords = new[]
        {
            "\u76ee\u524d",
            "\u73fe\u5728\u7684",
            "\u5373\u6642",
            "\u50f9\u683c"
        };

        if (!currentInfoKeywords.Any(input.Contains))
            return false;

        var localPetKeywords = new[]
        {
            "\u684c\u5bf5",
            "\u684c\u5ba0",
            "\u4f60\u73fe\u5728",
            "\u4f60\u7684\u72c0\u614b",
            "\u4f60\u7684\u72b6\u6001",
            "\u9ad4\u529b",
            "\u4f53\u529b",
            "\u5fc3\u60c5"
        };
        return !localPetKeywords.Any(input.Contains);
    }

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return WebUtility.HtmlDecode(value)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();
    }
}
