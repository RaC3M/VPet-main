using System.Threading;
using System.Threading.Tasks;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentSkillExecutor
{
    private readonly IMainWindow mw;
    private readonly CalendarReminderService reminderService;
    private readonly AiAgentPetStatusBuilder petStatusBuilder;
    private readonly WeatherSkillClient weatherClient = new();
    private readonly AiAgentMemoryStore memoryStore = new();
    private readonly LocalReminderStore localReminderStore = new();
    private readonly ProgramShortcutStore programShortcutStore = new();
    private readonly FileSearchSkill fileSearchSkill = new();

    public AiAgentSkillExecutor(IMainWindow mw, CalendarReminderService reminderService, AiAgentPetStatusBuilder petStatusBuilder)
    {
        this.mw = mw;
        this.reminderService = reminderService;
        this.petStatusBuilder = petStatusBuilder;
    }

    public async Task<string> ExecuteAsync(OllamaSkillCall skillCall, string userText, CancellationToken cancellationToken)
    {
        if (!skillCall.HasSkill)
            return "";

        var result = skillCall.SkillName.ToLowerInvariant() switch
        {
            "get_pet_status" => petStatusBuilder.BuildStatusSummary(),
            "get_calendar_events" => await reminderService.BuildCalendarSummaryAsync(cancellationToken),
            "get_weather" => await weatherClient.GetWeatherAsync(string.IsNullOrWhiteSpace(skillCall.Location) ? userText : skillCall.Location, cancellationToken),
            "remember_user_fact" => memoryStore.Remember(string.IsNullOrWhiteSpace(skillCall.Fact) ? userText : skillCall.Fact),
            "recall_memory" => memoryStore.Recall(),
            "create_reminder" => localReminderStore.Create(
                string.IsNullOrWhiteSpace(skillCall.Title) ? userText : skillCall.Title,
                skillCall.Time,
                skillCall.Note),
            "list_reminders" => localReminderStore.ListText(),
            "open_program" => programShortcutStore.Open(string.IsNullOrWhiteSpace(skillCall.Target) ? userText : skillCall.Target),
            "search_files" => await fileSearchSkill.SearchAsync(string.IsNullOrWhiteSpace(skillCall.Query) ? userText : skillCall.Query, cancellationToken),
            "sleep" => RunPetCommand("\u53bb\u7761\u89ba"),
            "wake_up" => RunPetCommand("\u9192\u4f86"),
            "stop_activity" => RunPetCommand("\u505c\u6b62\u5de5\u4f5c"),
            "touch_head" => RunPetCommand("\u6478\u982d"),
            "touch_body" => RunPetCommand("\u6478\u8eab\u9ad4"),
            "start_work" => RunPetCommand("\u958b\u59cb\u5de5\u4f5c"),
            "start_study" => RunPetCommand("\u958b\u59cb\u5b78\u7fd2"),
            "start_play" => RunPetCommand("\u53bb\u73a9"),
            "open_food_shop" => RunPetCommand("\u8cb7\u98df\u7269"),
            "open_drink_shop" => RunPetCommand("\u8cb7\u98f2\u6599"),
            _ => "\u672a\u77e5\u7684 skill\uff1a" + skillCall.SkillName
        };

        return string.IsNullOrWhiteSpace(result)
            ? ""
            : $"Skill `{skillCall.SkillName}` \u57f7\u884c\u7d50\u679c\uff1a\n{result}";
    }

    private string RunPetCommand(string command)
    {
        return AiAgentCommandRouter.TryHandle(mw, command, out var response)
            ? response
            : "\u9019\u500b skill \u76ee\u524d\u7121\u6cd5\u57f7\u884c\u3002";
    }
}
