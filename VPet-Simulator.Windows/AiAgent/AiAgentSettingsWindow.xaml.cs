using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LinePutScript;
using Panuon.WPF.UI;
using VPet_Simulator.Windows.Interface;
using static VPet_Simulator.Windows.Win32;

namespace VPet_Simulator.Windows.AiAgent;

public partial class AiAgentSettingsWindow : WindowX
{
    private readonly IMainWindow? mw;
    private readonly MainWindow? mainWindow;
    private readonly OpenAiAgentClient openAiClient = new();
    private readonly OllamaAgentClient ollamaClient = new();
    private readonly GoogleCalendarClient calendarClient = new();

    public AiAgentSettingsWindow(IMainWindow? mw = null)
    {
        this.mw = mw;
        mainWindow = mw as MainWindow;
        InitializeComponent();
        if (mw != null)
        {
            tbPetName.Text = mw.Core.Save.Name;
            tbOwnerName.Text = mw.GameSavesData.GameSave.HostName;
        }
        if (mainWindow != null)
            cbStartWithWindows.IsChecked = mainWindow.Set.StartUPBoot;
        else
            cbStartWithWindows.IsEnabled = false;
        SelectProvider(AiAgentEnvironment.Provider);
        cbOllamaAutoStart.IsChecked = AiAgentEnvironment.IsOllamaAutoStartEnabled;
        cbWebSearch.IsChecked = AiAgentEnvironment.IsWebSearchEnabled;
        tbVertexSearchProjectId.Text = AiAgentEnvironment.Get(AiAgentEnvironment.VertexSearchProjectId);
        tbVertexSearchAppId.Text = AiAgentEnvironment.Get(AiAgentEnvironment.VertexSearchAppId);
        tbVertexSearchLocation.Text = AiAgentEnvironment.Get(AiAgentEnvironment.VertexSearchLocation);
        if (string.IsNullOrWhiteSpace(tbVertexSearchLocation.Text))
            tbVertexSearchLocation.Text = "global";
        tbOllamaUrl.Text = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaUrl);
        if (string.IsNullOrWhiteSpace(tbOllamaUrl.Text))
            tbOllamaUrl.Text = "http://localhost:11434";
        var ollamaModel = AiAgentEnvironment.Get(AiAgentEnvironment.OllamaModel);
        SelectOllamaModel(string.IsNullOrWhiteSpace(ollamaModel) ? "qwen2.5:7b" : ollamaModel);
        tbOpenAiModel.Text = AiAgentEnvironment.Get(AiAgentEnvironment.OpenAiModel);
        if (string.IsNullOrWhiteSpace(tbOpenAiModel.Text))
            tbOpenAiModel.Text = "gpt-4.1-mini";
        tbGoogleClientId.Text = AiAgentEnvironment.Get(AiAgentEnvironment.GoogleClientId);
        UpdateStatus();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings(true);
    }

    private void SaveSettings(bool showMessage)
    {
        if (mw != null)
        {
            mw.Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(tbPetName.Text))
                    mw.Core.Save.Name = tbPetName.Text.Trim();
                mw.GameSavesData.GameSave.HostName = tbOwnerName.Text.Trim();
            });
        }
        var startupEnabled = cbStartWithWindows.IsChecked == true;
        if (mainWindow != null && startupEnabled != mainWindow.Set.StartUPBoot)
            ApplyWindowsStartupSetting();

        AiAgentEnvironment.SetUser(AiAgentEnvironment.AiProvider, GetSelectedProvider());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaAutoStart, cbOllamaAutoStart.IsChecked == false ? "false" : "true");
        AiAgentEnvironment.SetUser(AiAgentEnvironment.WebSearchEnabled, cbWebSearch.IsChecked == false ? "false" : "true");
        AiAgentEnvironment.SetUser(AiAgentEnvironment.VertexSearchProjectId, tbVertexSearchProjectId.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.VertexSearchAppId, tbVertexSearchAppId.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.VertexSearchLocation, string.IsNullOrWhiteSpace(tbVertexSearchLocation.Text) ? "global" : tbVertexSearchLocation.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaUrl, tbOllamaUrl.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.OllamaModel, GetSelectedOllamaModel());

        if (!string.IsNullOrWhiteSpace(pbVertexSearchApiKey.Password))
            AiAgentEnvironment.SetUser(AiAgentEnvironment.VertexSearchApiKey, pbVertexSearchApiKey.Password);

        if (!string.IsNullOrWhiteSpace(pbOpenAiKey.Password))
            AiAgentEnvironment.SetUser(AiAgentEnvironment.OpenAiApiKey, pbOpenAiKey.Password);

        AiAgentEnvironment.SetUser(AiAgentEnvironment.OpenAiModel, tbOpenAiModel.Text.Trim());
        AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleClientId, tbGoogleClientId.Text.Trim());

        if (!string.IsNullOrWhiteSpace(pbGoogleClientSecret.Password))
            AiAgentEnvironment.SetUser(AiAgentEnvironment.GoogleClientSecret, pbGoogleClientSecret.Password);

        pbVertexSearchApiKey.Clear();
        pbOpenAiKey.Clear();
        pbGoogleClientSecret.Clear();
        UpdateStatus();
        if (showMessage)
            MessageBoxX.Show("\u8a2d\u5b9a\u5df2\u5132\u5b58\u3002", "AI Agent");
    }

    private void ApplyWindowsStartupSetting()
    {
        if (mainWindow == null)
            return;

        var enabled = cbStartWithWindows.IsChecked == true;
        if (enabled && !mainWindow.Set.StartUPBoot)
        {
            var result = MessageBoxX.Show(
                "\u9019\u6703\u8b93\u684c\u5bf5\u5728 Windows \u958b\u6a5f\u6642\u81ea\u52d5\u555f\u52d5\u3002\n\u89e3\u9664\u5b89\u88dd\u524d\u8acb\u5148\u95dc\u9589\u9019\u500b\u9078\u9805\u3002",
                "Windows \u958b\u6a5f\u555f\u52d5",
                MessageBoxButton.YesNo,
                MessageBoxIcon.Warning);
            if (result != MessageBoxResult.Yes)
            {
                cbStartWithWindows.IsChecked = false;
                enabled = false;
            }
        }

        mainWindow.Set.StartUPBoot = enabled;
        GenerateWindowsStartupShortcut();
    }

    private void GenerateWindowsStartupShortcut()
    {
        if (mainWindow == null)
            return;

        mainWindow.Set["v"][(gbol)"newverstartup"] = true;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "VPET_Simulator.lnk");
        if (mainWindow.Set.StartUPBoot)
        {
            if (File.Exists(path))
                File.Delete(path);

            var link = (IShellLink)new ShellLink();
            if (mainWindow.Set.StartUPBootSteam)
            {
                link.SetPath(Path.Combine(ExtensionValue.BaseDirectory, "VPet.Solution.exe"));
                link.SetArguments("launchsteam");
            }
            else
            {
                link.SetPath(Assembly.GetExecutingAssembly().Location.Replace(".dll", ".exe"));
            }

            link.SetDescription("VPet Simulator");
            link.SetIconLocation(Path.Combine(ExtensionValue.BaseDirectory, "vpeticon.ico"), 0);

            try
            {
                var file = (IPersistFile)link;
                file.Save(path, false);
            }
            catch
            {
                MessageBoxX.Show("\u7121\u6cd5\u5efa\u7acb Windows \u958b\u6a5f\u555f\u52d5\u6377\u5f91\u3002\u8acb\u4ee5\u7ba1\u7406\u54e1\u8eab\u5206\u57f7\u884c\u5f8c\u518d\u8a66\u4e00\u6b21\u3002", "AI Agent");
            }
        }
        else if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private async void TestAi_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings(false);
        await RunButtonTaskAsync(async token =>
        {
            var response = GetSelectedProvider().Equals("openai", StringComparison.OrdinalIgnoreCase)
                ? await openAiClient.GetReplyAsync("\u8acb\u7528\u7e41\u9ad4\u4e2d\u6587\u7c21\u77ed\u56de\u7b54\uff1a\u6e2c\u8a66\u6210\u529f\u3002", "", "", token)
                : await ollamaClient.GetReplyAsync("\u8acb\u7528\u7e41\u9ad4\u4e2d\u6587\u7c21\u77ed\u56de\u7b54\uff1a\u6e2c\u8a66\u6210\u529f\u3002", "", "", token);
            MessageBoxX.Show(response, "AI \u6e2c\u8a66");
        });
    }

    private async void DownloadOllamaModel_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings(false);
        var model = GetSelectedOllamaModel();
        if (MessageBoxX.Show($"\u8981\u73fe\u5728\u4e0b\u8f09\u4e26\u90e8\u7f72 Ollama \u6a21\u578b `{model}` \u55ce\uff1f", "AI Agent", MessageBoxButton.YesNo, MessageBoxIcon.Info) != MessageBoxResult.Yes)
            return;

        await RunButtonTaskAsync(async token =>
        {
            var response = await ollamaClient.PullModelAsync(model, token);
            MessageBoxX.Show(response, "Ollama \u6a21\u578b");
        }, TimeSpan.FromHours(1));
    }

    private async void ConnectGoogle_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings(false);
        await RunButtonTaskAsync(async token =>
        {
            await calendarClient.ConnectAsync(token);
            UpdateStatus();
            MessageBoxX.Show("Google \u884c\u4e8b\u66c6\u5df2\u9023\u7dda\u3002", "AI Agent");
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task RunButtonTaskAsync(Func<CancellationToken, Task> action, TimeSpan? timeout = null)
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

    private void UpdateStatus()
    {
        var openAi = string.IsNullOrWhiteSpace(AiAgentEnvironment.Get(AiAgentEnvironment.OpenAiApiKey)) ? "\u672a\u8a2d\u5b9a" : "\u5df2\u8a2d\u5b9a";
        var google = string.IsNullOrWhiteSpace(AiAgentEnvironment.Get(AiAgentEnvironment.GoogleRefreshToken)) ? "\u672a\u9023\u7dda" : "\u5df2\u9023\u7dda";
        var ollamaAutoStart = AiAgentEnvironment.IsOllamaAutoStartEnabled ? "\u5df2\u555f\u7528" : "\u5df2\u95dc\u9589";
        var windowsStartup = mainWindow?.Set.StartUPBoot == true ? "\u5df2\u555f\u7528" : "\u5df2\u95dc\u9589";
        tbStatus.Text = $"AI \u4f86\u6e90\uff1a{AiAgentEnvironment.Provider}\nWindows \u958b\u6a5f\u555f\u52d5\uff1a{windowsStartup}\nOllama \u81ea\u52d5\u555f\u52d5\uff1a{ollamaAutoStart}\nOllama Agent Skills\uff1a\u5df2\u555f\u7528\nOpenAI Key\uff1a{openAi}\nGoogle \u884c\u4e8b\u66c6\uff1a{google}\nGoogle OAuth Redirect URI\uff1ahttp://127.0.0.1:53682/";
    }

    private string GetSelectedProvider()
    {
        return cbProvider.SelectedItem is ComboBoxItem item && item.Tag is string tag ? tag : "ollama";
    }

    private string GetSelectedOllamaModel()
    {
        if (cbOllamaModel.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && string.Equals(cbOllamaModel.Text, item.Content?.ToString(), StringComparison.Ordinal))
            return tag;
        return cbOllamaModel.Text.Trim();
    }

    private void SelectOllamaModel(string model)
    {
        foreach (var item in cbOllamaModel.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string tag
                && tag.Equals(model, StringComparison.OrdinalIgnoreCase))
            {
                cbOllamaModel.SelectedItem = comboBoxItem;
                return;
            }
        }

        cbOllamaModel.Text = model;
    }

    private void SelectProvider(string provider)
    {
        foreach (var item in cbProvider.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && comboBoxItem.Tag is string tag
                && tag.Equals(provider, StringComparison.OrdinalIgnoreCase))
            {
                cbProvider.SelectedItem = comboBoxItem;
                return;
            }
        }

        cbProvider.SelectedIndex = 0;
    }
}
