using System;
using System.Threading;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentTalkBox : TalkBox
{
    private readonly OpenAiAgentClient openAiClient;
    private readonly OllamaAgentClient ollamaClient;
    private readonly CalendarReminderService reminderService;
    private readonly AiAgentPetStatusBuilder petStatusBuilder;
    private readonly AiAgentSkillExecutor skillExecutor;

    public AiAgentTalkBox(MainPlugin mainPlugin, OpenAiAgentClient openAiClient, OllamaAgentClient ollamaClient, CalendarReminderService reminderService, AiAgentPetStatusBuilder petStatusBuilder)
        : base(mainPlugin)
    {
        this.openAiClient = openAiClient;
        this.ollamaClient = ollamaClient;
        this.reminderService = reminderService;
        this.petStatusBuilder = petStatusBuilder;
        skillExecutor = new AiAgentSkillExecutor(mainPlugin.MW, reminderService, petStatusBuilder);
    }

    public override string APIName => "AI Agent";

    public override void Responded(string text)
    {
        DisplayThink();
        try
        {
            if (AiAgentCommandRouter.TryHandle(MainPlugin.MW, text, out var commandResponse))
            {
                DisplayThinkToSayRnd(commandResponse, APIName);
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var petStatus = petStatusBuilder.BuildStatusSummary();
            var calendarSummary = reminderService.BuildCalendarSummaryAsync(cts.Token).GetAwaiter().GetResult();
            var isOpenAi = AiAgentEnvironment.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase);
            var skillResult = "";
            if (!isOpenAi)
            {
                var skillCall = ollamaClient.GetSkillCallAsync(text, petStatus, calendarSummary, cts.Token).GetAwaiter().GetResult();
                skillResult = skillExecutor.ExecuteAsync(skillCall, text, cts.Token).GetAwaiter().GetResult();
            }
            var contextSummary = BuildContextSummary(calendarSummary, skillResult);
            var response = isOpenAi
                ? openAiClient.GetReplyAsync(text, petStatus, contextSummary, cts.Token).GetAwaiter().GetResult()
                : ollamaClient.GetReplyAsync(text, petStatus, contextSummary, cts.Token).GetAwaiter().GetResult();
            DisplayThinkToSayRnd(string.IsNullOrWhiteSpace(response) ? "\u6211\u73fe\u5728\u9084\u60f3\u4e0d\u5230\u600e\u9ebc\u56de\u7b54\u55b5\u3002" : response, APIName);
        }
        catch (Exception ex)
        {
            DisplayThinkToSayRnd("\u6211\u7684 AI \u52a9\u624b\u51fa\u932f\u4e86\uff1a" + ex.Message, APIName);
        }
    }

    private static string BuildContextSummary(string calendarSummary, string skillResult)
    {
        var builder = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(calendarSummary))
            builder.AppendLine("Google \u884c\u4e8b\u66c6\u6458\u8981\uff1a").AppendLine(calendarSummary);
        if (!string.IsNullOrWhiteSpace(skillResult))
            builder.AppendLine(skillResult);
        return builder.ToString();
    }

    public override void Setting()
    {
        MainPlugin.MW.Dispatcher.Invoke(() =>
        {
            var window = new AiAgentSettingsWindow(MainPlugin.MW);
            window.Show();
            window.Activate();
        });
    }
}
