using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using LinePutScript;
using Panuon.WPF.UI;
using VPet_Simulator.Core;
using VPet_Simulator.Windows.Interface;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class AiAgentPlugin : MainPlugin
{
    private readonly OpenAiAgentClient openAiClient = new();
    private readonly OllamaAgentClient ollamaClient = new();
    private readonly GoogleCalendarClient calendarClient = new();
    private AiAgentPetStatusBuilder? petStatusBuilder;
    private CalendarReminderService? reminderService;
    private LocalReminderService? localReminderService;
    private AiAgentTalkBox? talkBox;

    public AiAgentPlugin(IMainWindow mainwin) : base(mainwin)
    {
    }

    public override string PluginName => "AI Agent";

    public override void LoadPlugin()
    {
        reminderService = new CalendarReminderService(MW, calendarClient);
        petStatusBuilder = new AiAgentPetStatusBuilder(MW);
        reminderService.Start();
        localReminderService = new LocalReminderService(MW, new LocalReminderStore());
        localReminderService.Start();

        talkBox = new AiAgentTalkBox(this, openAiClient, ollamaClient, reminderService, petStatusBuilder);
        MW.TalkAPI.Add(talkBox);

        MW.Main.ToolBar.AddMenuButton(ToolBar.MenuType.Interact, "AI Agent", ActivateTalkBox);
        MW.Main.ToolBar.AddMenuButton(ToolBar.MenuType.Setting, "AI Agent \u8a2d\u5b9a", Setting);
        MW.Main.ToolBar.AddMenuButton(ToolBar.MenuType.Setting, "AI Agent \u6280\u80fd\u8a2d\u5b9a", SkillSetting);
        MW.Main.ToolBar.AddMenuButton(ToolBar.MenuType.Setting, "\u9023\u7dda Google \u884c\u4e8b\u66c6", ConnectGoogleCalendar);

        if (AiAgentEnvironment.Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                await ollamaClient.EnsureReadyAsync(cts.Token);
            });
        }
    }

    public override void EndGame()
    {
        reminderService?.Dispose();
        localReminderService?.Dispose();
    }

    public override void Setting()
    {
        MW.Dispatcher.Invoke(() =>
        {
            var window = new AiAgentSettingsWindow(MW);
            window.Show();
            window.Activate();
        });
    }

    private void SkillSetting()
    {
        MW.Dispatcher.Invoke(() =>
        {
            var window = new AiAgentSkillSettingsWindow();
            window.Show();
            window.Activate();
        });
    }

    private void ActivateTalkBox()
    {
        if (talkBox == null || MW is not MainWindow mainWindow)
            return;

        var index = MW.TalkAPI.FindIndex(x => x.APIName == talkBox.APIName);
        if (index < 0)
            return;

        mainWindow.TalkAPIIndex = index;
        mainWindow.Set["CGPT"][(gstr)"type"] = "DIY";
        mainWindow.Set["CGPT"][(gstr)"DIY"] = talkBox.APIName;
        mainWindow.LoadTalkDIY();
    }

    private async void ConnectGoogleCalendar()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await calendarClient.ConnectAsync(cts.Token);
            MessageBoxX.Show("Google \u884c\u4e8b\u66c6\u5df2\u9023\u7dda\u3002\u684c\u5bf5\u6703\u5728\u884c\u7a0b\u524d\u5927\u7d04 10 \u5206\u9418\u63d0\u9192\u4f60\u3002", "AI Agent");
        }
        catch (Exception ex)
        {
            MessageBoxX.Show(ex.Message, "Google \u884c\u4e8b\u66c6\u9023\u7dda\u5931\u6557", MessageBoxButton.OK);
        }
    }
}
