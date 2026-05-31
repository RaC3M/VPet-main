using System;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Panuon.WPF.UI;

namespace VPet_Simulator.Windows.AiAgent;

public partial class AiAgentSkillSettingsWindow : WindowX
{
    private readonly LocalReminderStore reminderStore = new();
    private readonly ProgramShortcutStore shortcutStore = new();

    public AiAgentSkillSettingsWindow()
    {
        InitializeComponent();
        dpReminderDate.SelectedDate = DateTime.Now.Date;
        tbReminderTime.Text = DateTime.Now.AddMinutes(5).ToString("HH:mm");
        RefreshData();
    }

    private void AddReminder_Click(object sender, RoutedEventArgs e)
    {
        var date = dpReminderDate.SelectedDate ?? DateTime.Now.Date;
        var timeText = string.IsNullOrWhiteSpace(tbReminderTime.Text) ? DateTime.Now.AddMinutes(5).ToString("HH:mm") : tbReminderTime.Text.Trim();
        var fullTime = date.ToString("yyyy-MM-dd") + " " + timeText;
        var result = reminderStore.Create(tbReminderTitle.Text, fullTime, tbReminderNote.Text);
        MessageBoxX.Show(result, "AI Agent");
        tbReminderTitle.Clear();
        tbReminderNote.Clear();
        RefreshData();
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (dgReminders.SelectedItem is not LocalReminderInfo reminder)
        {
            MessageBoxX.Show("\u8acb\u5148\u9078\u64c7\u4e00\u7b46\u63d0\u9192\u3002", "AI Agent");
            return;
        }

        reminderStore.Delete(reminder.Id);
        RefreshData();
    }

    private void BrowseShortcut_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Programs and files|*.exe;*.lnk;*.bat;*.cmd;*.ps1;*.*|All files|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            tbShortcutPath.Text = dialog.FileName;
            if (string.IsNullOrWhiteSpace(tbShortcutName.Text))
                tbShortcutName.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        }
    }

    private void SaveShortcut_Click(object sender, RoutedEventArgs e)
    {
        var result = shortcutStore.AddOrUpdate(tbShortcutName.Text, tbShortcutPath.Text);
        MessageBoxX.Show(result, "AI Agent");
        tbShortcutName.Clear();
        tbShortcutPath.Clear();
        RefreshData();
    }

    private void DeleteShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (dgShortcuts.SelectedItem is not ProgramShortcutInfo shortcut)
        {
            MessageBoxX.Show("\u8acb\u5148\u9078\u64c7\u4e00\u7b46\u7a0b\u5f0f\u6377\u5f91\u3002", "AI Agent");
            return;
        }

        shortcutStore.Delete(shortcut.Name);
        RefreshData();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshData();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void RefreshData()
    {
        dgReminders.ItemsSource = null;
        dgReminders.ItemsSource = reminderStore.Load();
        dgShortcuts.ItemsSource = null;
        dgShortcuts.ItemsSource = shortcutStore.Load();
    }
}
