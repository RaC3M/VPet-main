using System;
using System.Threading;
using VPet_Simulator.Windows.AiAgent.Chat;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentTalkBox : TalkBox
{
    private readonly OpenAiAgentClient openAiClient;
    private readonly OllamaAgentClient ollamaClient;
    private readonly CalendarReminderService reminderService;
    private readonly AiAgentPetStatusBuilder petStatusBuilder;
    private readonly AiAgentSkillExecutor skillExecutor;
    private readonly PomodoroService pomodoroService;
    private readonly ShortTermMemorySkill shortTermMemorySkill = new();

    public AiAgentTalkBox(MainPlugin mainPlugin, OpenAiAgentClient openAiClient, OllamaAgentClient ollamaClient, CalendarReminderService reminderService, AiAgentPetStatusBuilder petStatusBuilder, PomodoroService pomodoroService)
        : base(mainPlugin)
    {
        this.openAiClient = openAiClient;
        this.ollamaClient = ollamaClient;
        this.reminderService = reminderService;
        this.petStatusBuilder = petStatusBuilder;
        this.pomodoroService = pomodoroService;
        skillExecutor = new AiAgentSkillExecutor(mainPlugin.MW, reminderService, petStatusBuilder, pomodoroService);
    }

    public override string APIName => "AI Agent";

    public override void Responded(string text)
    {
        DisplayThink();
        try
        {
            if (AiAgentCommandRouter.TryHandle(MainPlugin.MW, text, out var commandResponse, pomodoroService))
            {
                DisplayThinkToSayRnd(commandResponse, APIName);
                return;
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var result = CreatePipeline().RunAsync(text, cts.Token).GetAwaiter().GetResult();
            DisplayThinkToSayRnd(string.IsNullOrWhiteSpace(result.FinalResponse) ? "\u6211\u73fe\u5728\u9084\u60f3\u4e0d\u5230\u600e\u9ebc\u56de\u7b54\u55b5\u3002" : result.FinalResponse, APIName);
        }
        catch (Exception ex)
        {
            DisplayThinkToSayRnd("\u6211\u7684 AI \u52a9\u624b\u51fa\u932f\u4e86\uff1a" + ex.Message, APIName);
        }
    }

    private ChatPipeline CreatePipeline()
    {
        IAiReplyClient replyClient = AiAgentEnvironment.Provider.Equals("remote_api", StringComparison.OrdinalIgnoreCase)
            || AiAgentEnvironment.Provider.Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? openAiClient
                : ollamaClient;
        var memoryStore = new AiAgentMemoryStore();
        return new ChatPipeline(
            new ConversationContextBuilder(petStatusBuilder, reminderService),
            new EmotionSkill(),
            new IntentReasoningSkill(),
            new MemorySkill(memoryStore),
            new ToolSkill(skillExecutor),
            new PersonalitySkill(),
            new StyleSkill(),
            new ResponseReasoningSkill(),
            new ProactiveSkill(memoryStore),
            replyClient,
            shortTermMemorySkill);
    }

    public override void Setting()
    {
        MainPlugin.MW.Dispatcher.Invoke(() =>
        {
            if (MainPlugin.MW is MainWindow mainWindow)
                mainWindow.winSetting.SelectAiAgentSettings();
        });
    }
}
