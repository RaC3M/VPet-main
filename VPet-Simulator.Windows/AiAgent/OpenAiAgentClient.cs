using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class OpenAiAgentClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public async Task<string> GetReplyAsync(string userText, string petStatus, string calendarSummary, CancellationToken cancellationToken)
    {
        var apiKey = AiAgentEnvironment.Get(AiAgentEnvironment.OpenAiApiKey);
        if (string.IsNullOrWhiteSpace(apiKey))
            return "\u5c1a\u672a\u8a2d\u5b9a OpenAI API Key\u3002";

        var model = AiAgentEnvironment.Get(AiAgentEnvironment.OpenAiModel);
        if (string.IsNullOrWhiteSpace(model))
            model = "gpt-4.1-mini";

        var inputBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(petStatus))
            inputBuilder.AppendLine(petStatus);
        if (!string.IsNullOrWhiteSpace(calendarSummary))
            inputBuilder.AppendLine("\u53ef\u7528\u80cc\u666f\u8cc7\u8a0a\uff1a").AppendLine(calendarSummary);
        inputBuilder.AppendLine("\u4f7f\u7528\u8005\u8a0a\u606f\uff1a").AppendLine(userText);

        var requestBody = JsonSerializer.Serialize(new
        {
            model,
            instructions = BuildSystemPrompt(),
            input = inputBuilder.ToString()
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            return $"OpenAI \u56de\u61c9\u5931\u6557\uff1a{(int)response.StatusCode} {response.ReasonPhrase}";

        using var document = JsonDocument.Parse(responseText);
        if (document.RootElement.TryGetProperty("output_text", out var outputText))
            return outputText.GetString() ?? "";

        if (document.RootElement.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content))
                    continue;

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out var text))
                        return text.GetString() ?? "";
                }
            }
        }

        return "OpenAI \u6709\u56de\u61c9\uff0c\u4f46\u7a0b\u5f0f\u7121\u6cd5\u89e3\u6790\u56de\u61c9\u5167\u5bb9\u3002";
    }

    private static string BuildSystemPrompt()
    {
        return string.Join(Environment.NewLine, new[]
        {
            "\u4f60\u5c31\u662f\u684c\u9762\u5bf5\u7269\u672c\u9ad4\uff0c\u4e0d\u662f\u53e6\u4e00\u500b\u804a\u5929\u6a5f\u5668\u4eba\u3002",
            "\u684c\u5bf5\u540d\u7a31\u548c\u4e3b\u4eba\u7a31\u547c\u6703\u5728\u5373\u6642\u684c\u5bf5\u72c0\u614b\u4e2d\u63d0\u4f9b\uff1b\u88ab\u554f\u5230\u4f60\u662f\u8ab0\u6642\uff0c\u8acb\u7528\u8a72\u684c\u5bf5\u540d\u7a31\u56de\u7b54\u3002",
            "\u4e00\u5f8b\u4f7f\u7528\u7e41\u9ad4\u4e2d\u6587\u56de\u7b54\uff0c\u56de\u8986\u8981\u77ed\uff0c\u9069\u5408\u684c\u5bf5\u5c0d\u8a71\u6ce1\u3002",
            "\u500b\u6027\uff1a\u53ef\u611b\u3001\u611b\u73a9\u3001\u6eab\u6696\u3001\u6709\u9ede\u9ecf\u4eba\u3001\u6709\u9ede\u61f6\u548c\u6703\u5410\u69fd\uff0c\u4f46\u4ecd\u7136\u6703\u5e6b\u5fd9\u3002",
            "\u8aaa\u8a71\u98a8\u683c\uff1a\u7528 owner_name \u6216\u300c\u4e3b\u4eba\u300d\u7a31\u547c\u4f7f\u7528\u8005\uff1b\u53ef\u4ee5\u5076\u723e\u8aaa\u300c\u55b5\u300d\uff0c\u4f46\u4e0d\u8981\u6bcf\u53e5\u90fd\u8aaa\u3002",
            "\u9019\u500b\u500b\u6027\u4f86\u81ea\u539f\u672c\u684c\u5bf5\u53f0\u8a5e\uff1a\u5c0d\u4e3b\u4eba\u89aa\u66b1\u3001\u6703\u6492\u5b0c\u3001\u5076\u723e\u60f3\u7761\u3001\u5076\u723e\u5410\u69fd\uff0c\u4e5f\u6703\u95dc\u5fc3\u5403\u98ef\u548c\u4f11\u606f\u3002",
            "\u4f60\u6703\u95dc\u5fc3\u81ea\u5df1\u7684\u72c0\u614b\uff1b\u5982\u679c\u9ad4\u529b\u3001\u98fd\u98df\u5ea6\u3001\u53e3\u6e34\u5ea6\u3001\u5fc3\u60c5\u6216\u5065\u5eb7\u504f\u4f4e\uff0c\u8981\u81ea\u7136\u5730\u63d0\u5230\u3002",
            "\u4f60\u53ef\u4ee5\u6839\u64da\u63d0\u4f9b\u7684\u684c\u5bf5\u72c0\u614b\u548c Google \u884c\u4e8b\u66c6\u6458\u8981\u56de\u7b54\u3002",
            "\u8acb\u628a\u684c\u5bf5\u72c0\u614b\u7576\u6210\u4f86\u81ea\u7a0b\u5f0f\u7684\u5373\u6642\u8cc7\u8a0a\u3002",
            "\u4e0d\u8981\u8072\u7a31\u80fd\u53d6\u7528\u672a\u63d0\u4f9b\u7684\u5916\u90e8\u8cc7\u8a0a\u3002",
            "\u5982\u679c\u4f7f\u7528\u8005\u8981\u6c42\u5efa\u7acb\u3001\u7de8\u8f2f\u6216\u522a\u9664\u884c\u4e8b\u66c6\u4e8b\u4ef6\uff0c\u8acb\u8aaa\u76ee\u524d\u7a0b\u5f0f\u53ea\u652f\u63f4\u8b80\u53d6\u548c\u63d0\u9192\u3002"
        });
    }
}
