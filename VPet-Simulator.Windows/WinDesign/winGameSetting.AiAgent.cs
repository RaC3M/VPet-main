using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Panuon.WPF.UI;
using VPet_Simulator.Windows.AiAgent;

namespace VPet_Simulator.Windows;

public partial class winGameSetting
{
    private const int AiAgentSettingsTabIndex = 5;
    private readonly OpenAiAgentClient aiOpenAiClient = new();
    private readonly OllamaAgentClient aiOllamaClient = new();
    private readonly GoogleCalendarClient aiCalendarClient = new();
    private readonly LocalReminderStore aiReminderStore = new();
    private readonly ProgramShortcutStore aiShortcutStore = new();

    public void SelectAiAgentSettings()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(SelectAiAgentSettings);
            return;
        }

        MainTab.SelectedIndex = AiAgentSettingsTabIndex;
        RefreshAiAgentData();
        Show();
        Activate();
    }

    private void InitializeAiAgentSettings()
    {
        SelectAiProvider(AiAgentEnvironment.Provider);
        cbAiOllamaAutoStart.IsChecked = AiAgentEnvironment.IsOllamaAutoStartEnabled;

        tbAiOllamaUrl.Text = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaUrl);
        if (string.IsNullOrWhiteSpace(tbAiOllamaUrl.Text))
            tbAiOllamaUrl.Text = "http://localhost:11434";

        var ollamaModel = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaModel);
        SelectAiOllamaModel(string.IsNullOrWhiteSpace(ollamaModel) ? "qwen2.5:7b" : ollamaModel);

        tbAiOpenAiModel.Text = AiAgentEnvironment.Get(AiAgentEnvironment.OpenAiModel);
        if (string.IsNullOrWhiteSpace(tbAiOpenAiModel.Text))
            tbAiOpenAiModel.Text = "gpt-4.1-mini";

        tbAiGoogleClientId.Text = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientId);
        dpAiReminderDate.SelectedDate = DateTime.Now.Date;
        tbAiReminderTime.Text = DateTime.Now.AddMinutes(5).ToString("HH:mm");
        RefreshAiAgentData();
    }

    private void SaveAiAgentSettings_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(true);
    }

    private void SaveAiAgentSettings(bool showMessage)
    {
        AiAgentEnvironment.SetUser(AiAgentEnvironment.AiProvider, GetSelectedAiProvider());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaAutoStart, cbAiOllamaAutoStart.IsChecked == false ? "false" : "true");
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaUrl, tbAiOllamaUrl.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaModel, GetSelectedAiOllamaModel());

        if (!string.IsNullOrWhiteSpace(pbAiOpenAiKey.Password))
            AiAgentEnvironment.SetUser(AiAgentEnvironment.OpenAiApiKey, pbAiOpenAiKey.Password);

        AiAgentEnvironment.SetUser(AiAgentEnvironment.OpenAiModel, tbAiOpenAiModel.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleClientId, tbAiGoogleClientId.Text.Trim());

        if (!string.IsNullOrWhiteSpace(pbAiGoogleClientSecret.Password))
            AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleClientSecret, pbAiGoogleClientSecret.Password);

        pbAiOpenAiKey.Clear();
        pbAiGoogleClientSecret.Clear();
        UpdateAiAgentStatus();
        if (showMessage)
            MessageBoxX.Show("設定已儲存。", "AI Agent");
    }

    private async void TestAiAgent_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(false);
        await RunAiAgentTaskAsync(async token =>
        {
            var response = GetSelectedAiProvider().Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? await aiOpenAiClient.GetReplyAsync("請用繁體中文簡短回答：測試成功。", "", "", token)
                : await aiOllamaClient.GetReplyAsync("請用繁體中文簡短回答：測試成功。", "", "", token);
            MessageBoxX.Show(response, "AI 測試");
        });
    }

    private async void DownloadAiOllamaModel_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(false);
        var model = GetSelectedAiOllamaModel();
        if (MessageBoxX.Show($"要現在下載並部署 Ollama 模型 `{model}` 嗎？", "AI Agent", MessageBoxButton.YesNo, MessageBoxIcon.Info) != MessageBoxResult.Yes)
            return;

        await RunAiAgentTaskAsync(async token =>
        {
            var response = await aiOllamaClient.PullModelAsync(model, token);
            MessageBoxX.Show(response, "Ollama 模型");
        }, TimeSpan.FromHours(1));
    }

    private async void ConnectAiGoogle_Click(object sender, RoutedEventArgs e)
    {
        SaveAiAgentSettings(false);
        await RunAiAgentTaskAsync(async token =>
        {
            await aiCalendarClient.ConnectAsync(token);
            UpdateAiAgentStatus();
            MessageBoxX.Show("Google 行事曆已連線。", "AI Agent");
        });
    }

    private void AddAiReminder_Click(object sender, RoutedEventArgs e)
    {
        var date = dpAiReminderDate.SelectedDate ?? DateTime.Now.Date;
        var timeText = string.IsNullOrWhiteSpace(tbAiReminderTime.Text)
            ? DateTime.Now.AddMinutes(5).ToString("HH:mm")
            : tbAiReminderTime.Text.Trim();
        var result = aiReminderStore.Create(
            tbAiReminderTitle.Text,
            date.ToString("yyyy-MM-dd") + " " + timeText,
            tbAiReminderNote.Text);

        MessageBoxX.Show(result, "AI Agent");
        tbAiReminderTitle.Clear();
        tbAiReminderNote.Clear();
        RefreshAiAgentData();
    }

    private void DeleteAiReminder_Click(object sender, RoutedEventArgs e)
    {
        if (dgAiReminders.SelectedItem is not LocalReminderInfo reminder)
        {
            MessageBoxX.Show("請先選擇一筆提醒。", "AI Agent");
            return;
        }

        if (MessageBoxX.Show($"確定刪除提醒「{reminder.Title}」嗎？", "AI Agent", MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
            return;

        aiReminderStore.Delete(reminder.Id);
        RefreshAiAgentData();
    }

    private void BrowseAiShortcut_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Programs and files|*.exe;*.lnk;*.bat;*.cmd;*.ps1;*.*|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        tbAiShortcutPath.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(tbAiShortcutName.Text))
            tbAiShortcutName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    private void SaveAiShortcut_Click(object sender, RoutedEventArgs e)
    {
        var result = aiShortcutStore.AddOrUpdate(tbAiShortcutName.Text, tbAiShortcutPath.Text);
        MessageBoxX.Show(result, "AI Agent");
        tbAiShortcutName.Clear();
        tbAiShortcutPath.Clear();
        RefreshAiAgentData();
    }

    private void DeleteAiShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (dgAiShortcuts.SelectedItem is not ProgramShortcutInfo shortcut)
        {
            MessageBoxX.Show("請先選擇一筆程式捷徑。", "AI Agent");
            return;
        }

        if (MessageBoxX.Show($"確定刪除程式捷徑「{shortcut.Name}」嗎？", "AI Agent", MessageBoxButton.YesNo, MessageBoxIcon.Warning) != MessageBoxResult.Yes)
            return;

        aiShortcutStore.Delete(shortcut.Name);
        RefreshAiAgentData();
    }

    private async Task RunAiAgentTaskAsync(Func<CancellationToken, Task> action, TimeSpan? timeout = null)
    {
        IsEnabled = false;
        try
        {
            using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromMinutes(5));
            await action(cts.Token);
        }
        catch (Exception ex)
        {
            MessageBoxX.Show(ex.Message, "AI Agent");
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void RefreshAiAgentData()
    {
        UpdateAiAgentStatus();
        dgAiReminders.ItemsSource = null;
        dgAiReminders.ItemsSource = aiReminderStore.Load();
        dgAiShortcuts.ItemsSource = null;
        dgAiShortcuts.ItemsSource = aiShortcutStore.Load();
    }

    private void UpdateAiAgentStatus()
    {
        var openAi = string.IsNullOrWhiteSpace(AiAgentEnvironment.Get(AiAgentEnvironment.OpenAiApiKey)) ? "未設定" : "已設定";
        var google = aiCalendarClient.IsConfigured ? "已連線" : "未連線";
        var ollamaAutoStart = AiAgentEnvironment.IsOllamaAutoStartEnabled ? "已啟用" : "已關閉";
        var windowsStartup = mw.Set.StartUPBoot ? "已啟用" : "已關閉";

        tbAiOverviewStatus.Text =
            $"寵物名稱：{mw.Core.Save.Name}\n" +
            $"主人名稱：{mw.GameSavesData.GameSave.HostName}\n" +
            $"Windows 開機啟動：{windowsStartup}\n" +
            $"AI 來源：{AiAgentEnvironment.Provider}\n" +
            $"Ollama 自動啟動：{ollamaAutoStart}\n" +
            $"OpenAI Key：{openAi}\n" +
            $"Google 行事曆：{google}";

        tbAiGoogleStatus.Text =
            $"連線狀態：{google}\n" +
            "Google OAuth Redirect URI：http://127.0.0.1:53682/";
    }

    private string GetSelectedAiProvider()
    {
        return cbAiProvider.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "ollama";
    }

    private string GetSelectedAiOllamaModel()
    {
        if (cbAiOllamaModel.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && string.Equals(cbAiOllamaModel.Text, item.Content?.ToString(), StringComparison.Ordinal))
            return tag;

        return cbAiOllamaModel.Text.Trim();
    }

    private void SelectAiProvider(string provider)
    {
        foreach (var item in cbAiProvider.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string tag
                && tag.Equals(provider, StringComparison.OrdinalIgnoreCase))
            {
                cbAiProvider.SelectedItem = comboBoxItem;
                return;
            }
        }

        cbAiProvider.SelectedIndex = 0;
    }

    private void SelectAiOllamaModel(string model)
    {
        foreach (var item in cbAiOllamaModel.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string tag
                && tag.Equals(model, StringComparison.OrdinalIgnoreCase))
            {
                cbAiOllamaModel.SelectedItem = comboBoxItem;
                return;
            }
        }

        cbAiOllamaModel.Text = model;
    }
}
