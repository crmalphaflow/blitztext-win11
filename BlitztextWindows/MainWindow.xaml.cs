using System.ComponentModel;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace BlitztextWindows;

public partial class MainWindow : Window, IDisposable
{
    private readonly SettingsStore settingsStore = new();
    private readonly SecureStore secureStore = new();
    private readonly AudioRecorderService recorder = new();
    private readonly OpenAIClient openAIClient = new();
    private readonly ClipboardPasteService pasteService = new();
    private readonly HotkeyService hotkeys = new();
    private Forms.NotifyIcon? notifyIcon;
    private GhostWindow? ghostWindow;
    private AppSettings settings = new();
    private WorkflowKind activeWorkflow;
    private WorkflowKind? learningWorkflow;
    private bool learningGhostHotkey;
    private bool learningTextShortcutHotkey;
    private IntPtr windowHandle;
    private bool workflowRunning;
    private bool disposed;
    private bool suppressModeEditorEvents;
    private bool suppressTextShortcutEditorEvents;
    private bool suppressAppSettingEvents;

    public MainWindow()
    {
        InitializeComponent();
        settings = settingsStore.Load();
        settings.EnsureDefaults();
        InitializeModeEditor();
        InitializeTextShortcutEditor();
        InitializeAppSettings();
        RefreshKeyHint();
        RefreshApiKeyEditor();
        RefreshWorkflowLabels();
        RefreshHotkeyLabels();
        RefreshPasteModeText();
        CreateTrayIcon();

        PreviewKeyDown += (_, e) =>
        {
            if (learningWorkflow is { } kind)
            {
                if (e.Key == Key.Escape)
                {
                    learningWorkflow = null;
                    SetStatus("Tastenlernen abgebrochen.", AppVisualState.Ready);
                }
                else
                {
                    SaveLearnedHotkey(kind, e.Key == Key.System ? e.SystemKey : e.Key);
                }

                e.Handled = true;
                return;
            }

            if (learningGhostHotkey)
            {
                if (e.Key == Key.Escape)
                {
                    learningGhostHotkey = false;
                    SetStatus("Ghost-Taste lernen abgebrochen.", AppVisualState.Ready);
                }
                else
                {
                    SaveLearnedGhostHotkey(e.Key == Key.System ? e.SystemKey : e.Key);
                }

                e.Handled = true;
                return;
            }

            if (learningTextShortcutHotkey)
            {
                if (e.Key == Key.Escape)
                {
                    learningTextShortcutHotkey = false;
                    SetStatus("Textbaustein-Taste lernen abgebrochen.", AppVisualState.Ready);
                }
                else
                {
                    SaveLearnedTextShortcutHotkey(e.Key == Key.System ? e.SystemKey : e.Key);
                }

                e.Handled = true;
                return;
            }

            if (e.Key == Key.Escape)
            {
                CancelWorkflow();
            }
        };

        SourceInitialized += (_, _) => RegisterHotkeys();
        Loaded += (_, _) =>
        {
            if (settings.StartInGhostMode)
            {
                EnterGhostMode();
            }
        };
    }

    private void RegisterHotkeys()
    {
        var source = (HwndSource)PresentationSource.FromVisual(this);
        windowHandle = source.Handle;
        hotkeys.Register(windowHandle, settings.Hotkeys, settings.TextShortcuts);
        hotkeys.HotkeyPressed += async (_, kind) => await Dispatcher.InvokeAsync(() => ToggleWorkflowAsync(kind));
        hotkeys.GhostPressed += (_, _) => Dispatcher.Invoke(ToggleGhostMode);
        hotkeys.TextShortcutPressed += (_, shortcutId) => Dispatcher.Invoke(() => PasteTextShortcut(shortcutId));
    }

    private void CreateTrayIcon()
    {
        notifyIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Blitztext",
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };

        RefreshTrayMenu();
        notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void RefreshTrayMenu()
    {
        if (notifyIcon?.ContextMenuStrip is null)
        {
            return;
        }

        var menu = notifyIcon.ContextMenuStrip;
        menu.Items.Clear();
        menu.Items.Add($"1  {ModeName(WorkflowKind.Transcribe)}", null, async (_, _) => await Dispatcher.InvokeAsync(() => ToggleWorkflowAsync(WorkflowKind.Transcribe)));
        menu.Items.Add($"2  {ModeName(WorkflowKind.Improve)}", null, async (_, _) => await Dispatcher.InvokeAsync(() => ToggleWorkflowAsync(WorkflowKind.Improve)));
        menu.Items.Add($"3  {ModeName(WorkflowKind.Calm)}", null, async (_, _) => await Dispatcher.InvokeAsync(() => ToggleWorkflowAsync(WorkflowKind.Calm)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        foreach (var shortcut in settings.TextShortcuts.OrderBy(shortcut => shortcut.Id))
        {
            menu.Items.Add($"Text: {shortcut.Name}", null, (_, _) => Dispatcher.Invoke(() => PasteTextShortcut(shortcut.Id)));
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Ghost-Modus", null, (_, _) => Dispatcher.Invoke(ToggleGhostMode));
        menu.Items.Add("Oeffnen", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Beenden", null, (_, _) => Dispatcher.Invoke(() => System.Windows.Application.Current.Shutdown()));
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/Blitztext.ico"));
        if (resource is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var icon = new System.Drawing.Icon(resource.Stream);
        return (System.Drawing.Icon)icon.Clone();
    }

    private void ShowFromTray()
    {
        ExitGhostMode();
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private async void Workflow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && Enum.TryParse<WorkflowKind>((string?)element.Tag, out var kind))
        {
            await ToggleWorkflowAsync(kind);
        }
    }

    private async Task ToggleWorkflowAsync(WorkflowKind kind)
    {
        if (workflowRunning)
        {
            await StopAndProcessAsync();
            return;
        }

        var apiKey = secureStore.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetStatus("API Key fehlt.", AppVisualState.Error);
            ShowFromTray();
            ShowApiKeyEditor();
            ApiKeyBox.Focus();
            return;
        }

        activeWorkflow = kind;
        workflowRunning = true;
        OutputBox.Text = "";
        RawTranscriptBox.Text = "";
        ResultsTabs.SelectedIndex = 0;
        RefreshPasteModeText();
        SetStatus("Aufnahme laeuft ...", AppVisualState.Recording);
        PlaySound(AppSound.Start);

        try
        {
            recorder.Start();
        }
        catch (Exception ex)
        {
            workflowRunning = false;
            SetStatus("Aufnahme konnte nicht gestartet werden.", AppVisualState.Error);
            OutputBox.Text = FriendlyError(ex);
            PlaySound(AppSound.Error);
        }
    }

    private async Task StopAndProcessAsync()
    {
        workflowRunning = false;
        string filePath;
        TimeSpan duration;

        try
        {
            var recording = recorder.Stop();
            filePath = recording.FilePath;
            duration = recording.Duration;
        }
        catch (Exception ex)
        {
            SetStatus("Aufnahme konnte nicht beendet werden.", AppVisualState.Error);
            OutputBox.Text = FriendlyError(ex);
            PlaySound(AppSound.Error);
            return;
        }

        if (duration.TotalMilliseconds < 650)
        {
            TryDelete(filePath);
            SetStatus("Keine Aufnahme erkannt.", AppVisualState.Error);
            PlaySound(AppSound.Error);
            return;
        }

        try
        {
            var mode = GetMode(activeWorkflow);
            SetStatus(activeWorkflow == WorkflowKind.Transcribe ? "Wird transkribiert ..." : "Wird verarbeitet ...", AppVisualState.Processing);
            var apiKey = secureStore.LoadApiKey() ?? "";
            var transcript = await openAIClient.TranscribeAsync(apiKey, filePath, EffectiveLanguage(mode));
            RawTranscriptBox.Text = transcript;

            var output = activeWorkflow == WorkflowKind.Transcribe
                ? transcript
                : await openAIClient.RewriteAsync(
                    apiKey,
                    transcript,
                    EffectivePrompt(activeWorkflow, mode),
                    OpenAIModels.Rewrite,
                    ClampTemperature(mode.Temperature));

            OutputBox.Text = output;
            System.Windows.Clipboard.SetText(output);

            if (mode.AutoPaste)
            {
                pasteService.PasteClipboardText();
            }

            SetStatus("Fertig.", AppVisualState.Success);
            PlaySound(AppSound.Done);
        }
        catch (Exception ex)
        {
            SetStatus("Fehler.", AppVisualState.Error);
            OutputBox.Text = FriendlyError(ex);
            PlaySound(AppSound.Error);
        }
        finally
        {
            TryDelete(filePath);
        }
    }

    private void CancelWorkflow()
    {
        if (!workflowRunning)
        {
            return;
        }

        workflowRunning = false;
        recorder.Cancel();
        SetStatus("Abgebrochen.", AppVisualState.Ready);
        PlaySound(AppSound.Done);
    }

    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        var apiKey = ApiKeyBox.Password.Trim();
        if (apiKey.Length == 0)
        {
            SetStatus("API Key ist leer.", AppVisualState.Error);
            return;
        }

        secureStore.SaveApiKey(apiKey);
        ApiKeyBox.Clear();
        RefreshKeyHint();
        RefreshApiKeyEditor();
        SetStatus("API Key gespeichert.", AppVisualState.Success);
    }

    private void DeleteApiKey_Click(object sender, RoutedEventArgs e)
    {
        secureStore.DeleteApiKey();
        RefreshKeyHint();
        ShowApiKeyEditor();
        SetStatus("API Key geloescht.", AppVisualState.Ready);
    }

    private void ToggleApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (ApiKeyEditor.Visibility == Visibility.Visible)
        {
            HideApiKeyEditor();
        }
        else
        {
            ShowApiKeyEditor();
        }
    }

    private void CancelApiKey_Click(object sender, RoutedEventArgs e)
    {
        ApiKeyBox.Clear();
        RefreshApiKeyEditor();
    }

    private void LearnHotkey_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            element.Tag is not string tag)
        {
            return;
        }

        if (tag == "Ghost")
        {
            learningGhostHotkey = true;
            ShowFromTray();
            Focus();
            SetStatus("Jetzt die Ghost-Maustaste druecken ...", AppVisualState.Ready);
            return;
        }

        if (tag == "TextShortcut")
        {
            learningTextShortcutHotkey = true;
            ShowFromTray();
            Focus();
            SetStatus("Jetzt die Taste fuer den Textbaustein druecken ...", AppVisualState.Ready);
            return;
        }

        if (!Enum.TryParse<WorkflowKind>(tag, out var kind))
        {
            return;
        }

        learningWorkflow = kind;
        ShowFromTray();
        Focus();
        SetStatus($"{ModeName(kind)}: Jetzt die gewuenschte Razer-Taste druecken ...", AppVisualState.Ready);
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        settings.Hotkeys = new HotkeyBindings();
        SaveSettings();
        RefreshHotkeyLabels();
        ReregisterHotkeys();
        SetStatus("Hotkeys auf Standard gesetzt.", AppVisualState.Ready);
    }

    private void SaveLearnedHotkey(WorkflowKind kind, Key key)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            SetStatus("Diese Taste konnte Windows nicht als Hotkey erkennen.", AppVisualState.Error);
            return;
        }

        switch (kind)
        {
            case WorkflowKind.Transcribe:
                settings.Hotkeys.Transcribe = virtualKey;
                break;
            case WorkflowKind.Improve:
                settings.Hotkeys.Improve = virtualKey;
                break;
            case WorkflowKind.Calm:
                settings.Hotkeys.Calm = virtualKey;
                break;
        }

        learningWorkflow = null;
        SaveSettings();
        RefreshHotkeyLabels();
        ReregisterHotkeys();
        SetStatus($"{ModeName(kind)} liegt jetzt auf {HotkeyName(virtualKey)}.", AppVisualState.Success);
    }

    private void SaveLearnedGhostHotkey(Key key)
    {
        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            SetStatus("Diese Taste konnte Windows nicht als Hotkey erkennen.", AppVisualState.Error);
            return;
        }

        learningGhostHotkey = false;
        settings.Hotkeys.Ghost = virtualKey;
        SaveSettings();
        RefreshHotkeyLabels();
        ReregisterHotkeys();
        SetStatus($"Ghost-Modus liegt jetzt auf {HotkeyName(virtualKey)}.", AppVisualState.Success);
    }

    private void SaveLearnedTextShortcutHotkey(Key key)
    {
        var shortcut = SelectedTextShortcut();
        if (shortcut is null)
        {
            return;
        }

        var virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            SetStatus("Diese Taste konnte Windows nicht als Hotkey erkennen.", AppVisualState.Error);
            return;
        }

        learningTextShortcutHotkey = false;
        shortcut.Hotkey = virtualKey;
        SaveSettings();
        RefreshTextShortcutLabels();
        ReregisterHotkeys();
        SetStatus($"{shortcut.Name} liegt jetzt auf {HotkeyName(virtualKey)}.", AppVisualState.Success);
    }

    private void ReregisterHotkeys()
    {
        if (windowHandle != IntPtr.Zero)
        {
            hotkeys.Register(windowHandle, settings.Hotkeys, settings.TextShortcuts);
        }
    }

    private void CopyOutput_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(OutputBox.Text))
        {
            System.Windows.Clipboard.SetText(OutputBox.Text);
            SetStatus("In Zwischenablage kopiert.", AppVisualState.Success);
        }
    }

    private void InitializeModeEditor()
    {
        suppressModeEditorEvents = true;
        ModeEditorCombo.Items.Clear();
        AddModeEditorItem(WorkflowKind.Transcribe);
        AddModeEditorItem(WorkflowKind.Improve);
        AddModeEditorItem(WorkflowKind.Calm);
        ModeEditorCombo.SelectedIndex = 0;
        LoadModeEditor(WorkflowKind.Transcribe);
        suppressModeEditorEvents = false;
    }

    private void AddModeEditorItem(WorkflowKind kind)
    {
        ModeEditorCombo.Items.Add(new ComboBoxItem
        {
            Content = ModeName(kind),
            Tag = kind
        });
    }

    private void ModeEditorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressModeEditorEvents || SelectedEditorMode() is not { } kind)
        {
            return;
        }

        LoadModeEditor(kind);
    }

    private void LoadModeEditor(WorkflowKind kind)
    {
        suppressModeEditorEvents = true;
        var mode = GetMode(kind);
        ModeNameBox.Text = mode.Name;
        ModeLanguageBox.Text = mode.Language;
        ModePromptBox.Text = mode.Prompt;
        ModeTemperatureSlider.Value = ClampTemperature(mode.Temperature);
        ModeAutoPasteBox.IsChecked = mode.AutoPaste;

        var usesPrompt = kind != WorkflowKind.Transcribe;
        ModePromptBox.IsEnabled = usesPrompt;
        ModeTemperatureSlider.IsEnabled = usesPrompt;
        ModeTemperatureText.Text = ClampTemperature(mode.Temperature).ToString("0.00");
        suppressModeEditorEvents = false;
    }

    private WorkflowKind? SelectedEditorMode()
    {
        return (ModeEditorCombo.SelectedItem as ComboBoxItem)?.Tag as WorkflowKind?;
    }

    private void ModeEditor_TextChanged(object sender, TextChangedEventArgs e)
    {
        SaveModeEditor();
    }

    private void ModeEditor_CheckChanged(object sender, RoutedEventArgs e)
    {
        SaveModeEditor();
    }

    private void ModeTemperatureSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ModeTemperatureText is not null)
        {
            ModeTemperatureText.Text = e.NewValue.ToString("0.00");
        }

        SaveModeEditor();
    }

    private void SaveModeEditor()
    {
        if (suppressModeEditorEvents || SelectedEditorMode() is not { } kind)
        {
            return;
        }

        var mode = GetMode(kind);
        mode.Name = string.IsNullOrWhiteSpace(ModeNameBox.Text) ? DefaultModeName(kind) : ModeNameBox.Text.Trim();
        mode.Language = string.IsNullOrWhiteSpace(ModeLanguageBox.Text) ? "de" : ModeLanguageBox.Text.Trim();
        mode.Prompt = ModePromptBox.Text;
        mode.Temperature = ClampTemperature(ModeTemperatureSlider.Value);
        mode.AutoPaste = ModeAutoPasteBox.IsChecked == true;

        SaveSettings();
        RefreshWorkflowLabels();
        RefreshHotkeyLabels();
        RefreshModeEditorComboLabels();
        RefreshTrayMenu();
        RefreshPasteModeText();
    }

    private void ResetModes_Click(object sender, RoutedEventArgs e)
    {
        settings.Modes = ModeSettings.CreateDefaults();
        SaveSettings();
        RefreshWorkflowLabels();
        RefreshHotkeyLabels();
        RefreshModeEditorComboLabels();
        RefreshTrayMenu();
        LoadModeEditor(SelectedEditorMode() ?? WorkflowKind.Transcribe);
        RefreshPasteModeText();
        SetStatus("Modi auf Standard gesetzt.", AppVisualState.Ready);
    }

    private void RefreshModeEditorComboLabels()
    {
        foreach (ComboBoxItem item in ModeEditorCombo.Items)
        {
            if (item.Tag is WorkflowKind kind)
            {
                item.Content = ModeName(kind);
            }
        }
    }

    private void InitializeTextShortcutEditor()
    {
        suppressTextShortcutEditorEvents = true;
        TextShortcutCombo.Items.Clear();
        foreach (var shortcut in settings.TextShortcuts.OrderBy(shortcut => shortcut.Id))
        {
            TextShortcutCombo.Items.Add(new ComboBoxItem
            {
                Content = shortcut.Name,
                Tag = shortcut.Id
            });
        }

        TextShortcutCombo.SelectedIndex = 0;
        LoadTextShortcutEditor(SelectedTextShortcut());
        suppressTextShortcutEditorEvents = false;
    }

    private void TextShortcutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressTextShortcutEditorEvents)
        {
            return;
        }

        LoadTextShortcutEditor(SelectedTextShortcut());
    }

    private TextShortcutSettings? SelectedTextShortcut()
    {
        var id = (TextShortcutCombo.SelectedItem as ComboBoxItem)?.Tag as int?;
        return id is null
            ? null
            : settings.TextShortcuts.FirstOrDefault(shortcut => shortcut.Id == id.Value);
    }

    private void LoadTextShortcutEditor(TextShortcutSettings? shortcut)
    {
        if (shortcut is null)
        {
            return;
        }

        suppressTextShortcutEditorEvents = true;
        TextShortcutNameBox.Text = shortcut.Name;
        TextShortcutValueBox.Text = SecureTextProtector.Unprotect(shortcut.ProtectedText);
        TextShortcutHotkeyText.Text = $"Taste: {HotkeyName(shortcut.Hotkey)}";
        suppressTextShortcutEditorEvents = false;
    }

    private void TextShortcut_TextChanged(object sender, TextChangedEventArgs e)
    {
        SaveTextShortcutEditor();
    }

    private void SaveTextShortcutEditor()
    {
        if (suppressTextShortcutEditorEvents || SelectedTextShortcut() is not { } shortcut)
        {
            return;
        }

        shortcut.Name = string.IsNullOrWhiteSpace(TextShortcutNameBox.Text)
            ? $"Textbaustein {shortcut.Id}"
            : TextShortcutNameBox.Text.Trim();
        shortcut.ProtectedText = SecureTextProtector.Protect(TextShortcutValueBox.Text);
        SaveSettings();
        RefreshTextShortcutLabels();
        RefreshTrayMenu();
    }

    private void RefreshTextShortcutLabels()
    {
        foreach (ComboBoxItem item in TextShortcutCombo.Items)
        {
            if (item.Tag is int id &&
                settings.TextShortcuts.FirstOrDefault(shortcut => shortcut.Id == id) is { } shortcut)
            {
                item.Content = shortcut.Name;
            }
        }

        if (SelectedTextShortcut() is { } selected)
        {
            TextShortcutHotkeyText.Text = $"Taste: {HotkeyName(selected.Hotkey)}";
        }
    }

    private void PasteTextShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTextShortcut() is { } shortcut)
        {
            PasteTextShortcut(shortcut.Id);
        }
    }

    private void ClearTextShortcut_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTextShortcut() is not { } shortcut)
        {
            return;
        }

        shortcut.ProtectedText = "";
        TextShortcutValueBox.Text = "";
        SaveSettings();
        SetStatus($"{shortcut.Name} geleert.", AppVisualState.Ready);
    }

    private void PasteTextShortcut(int shortcutId)
    {
        var shortcut = settings.TextShortcuts.FirstOrDefault(item => item.Id == shortcutId);
        if (shortcut is null)
        {
            return;
        }

        var value = SecureTextProtector.Unprotect(shortcut.ProtectedText);
        if (string.IsNullOrEmpty(value))
        {
            SetStatus($"{shortcut.Name} ist leer.", AppVisualState.Error);
            PlaySound(AppSound.Error);
            return;
        }

        System.Windows.Clipboard.SetText(value);
        pasteService.PasteClipboardText();
        SetStatus($"{shortcut.Name} eingefuegt.", AppVisualState.Success);
        PlaySound(AppSound.Done);
    }

    private void InitializeAppSettings()
    {
        suppressAppSettingEvents = true;
        SoundEnabledBox.IsChecked = settings.PlaySounds;
        StartWithWindowsBox.IsChecked = StartupService.IsEnabled();
        StartInGhostModeBox.IsChecked = settings.StartInGhostMode;
        suppressAppSettingEvents = false;
    }

    private void SoundEnabledBox_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressAppSettingEvents)
        {
            return;
        }

        settings.PlaySounds = SoundEnabledBox.IsChecked == true;
        SaveSettings();
    }

    private void StartWithWindowsBox_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressAppSettingEvents)
        {
            return;
        }

        try
        {
            StartupService.SetEnabled(StartWithWindowsBox.IsChecked == true);
            SetStatus(StartWithWindowsBox.IsChecked == true ? "Autostart aktiviert." : "Autostart deaktiviert.", AppVisualState.Success);
        }
        catch (Exception ex)
        {
            SetStatus("Autostart konnte nicht geaendert werden.", AppVisualState.Error);
            OutputBox.Text = FriendlyError(ex);
        }
    }

    private void StartInGhostModeBox_Changed(object sender, RoutedEventArgs e)
    {
        if (suppressAppSettingEvents)
        {
            return;
        }

        settings.StartInGhostMode = StartInGhostModeBox.IsChecked == true;
        SaveSettings();
        SetStatus(settings.StartInGhostMode ? "Start im Ghost-Modus aktiviert." : "Start im Ghost-Modus deaktiviert.", AppVisualState.Success);
    }

    private void ToggleGhost_Click(object sender, RoutedEventArgs e)
    {
        ToggleGhostMode();
    }

    private void ToggleGhostMode()
    {
        if (IsGhostModeActive)
        {
            ShowFromTray();
        }
        else
        {
            EnterGhostMode();
        }
    }

    private bool IsGhostModeActive => ghostWindow?.IsVisible == true && !IsVisible;

    private void EnterGhostMode()
    {
        EnsureGhostWindow();
        ghostWindow!.SetState(AppVisualState.Ready);
        ghostWindow.Show();
        Hide();
        SetStatus("Ghost-Modus aktiv.", AppVisualState.Ready);
    }

    private void ExitGhostMode()
    {
        ghostWindow?.Hide();
    }

    private void EnsureGhostWindow()
    {
        if (ghostWindow is not null)
        {
            return;
        }

        ghostWindow = new GhostWindow();
        ghostWindow.RestoreRequested += (_, _) => ShowFromTray();
    }

    private void RefreshKeyHint()
    {
        KeyHintText.Text = secureStore.HasApiKey()
            ? "API Key gespeichert. Du kannst Blitztext direkt nutzen."
            : "Noch kein API Key gespeichert.";
    }

    private void RefreshApiKeyEditor()
    {
        if (secureStore.HasApiKey())
        {
            HideApiKeyEditor();
        }
        else
        {
            ShowApiKeyEditor();
        }
    }

    private void ShowApiKeyEditor()
    {
        ApiKeyEditor.Visibility = Visibility.Visible;
        ToggleApiKeyButton.Content = "Ausblenden";
    }

    private void HideApiKeyEditor()
    {
        ApiKeyEditor.Visibility = Visibility.Collapsed;
        ToggleApiKeyButton.Content = "API Key aendern";
    }

    private void RefreshWorkflowLabels()
    {
        Mode1Button.Content = $"1  {ModeName(WorkflowKind.Transcribe)}";
        Mode2Button.Content = $"2  {ModeName(WorkflowKind.Improve)}";
        Mode3Button.Content = $"3  {ModeName(WorkflowKind.Calm)}";
    }

    private void RefreshHotkeyLabels()
    {
        TranscribeHotkeyText.Text = $"1: {ModeName(WorkflowKind.Transcribe)} ({HotkeyName(settings.Hotkeys.Transcribe)})";
        ImproveHotkeyText.Text = $"2: {ModeName(WorkflowKind.Improve)} ({HotkeyName(settings.Hotkeys.Improve)})";
        CalmHotkeyText.Text = $"3: {ModeName(WorkflowKind.Calm)} ({HotkeyName(settings.Hotkeys.Calm)})";
        GhostHotkeyText.Text = $"Ghost: Mini-Mikro ({HotkeyName(settings.Hotkeys.Ghost)})";
    }

    private void RefreshPasteModeText()
    {
        var mode = GetMode(activeWorkflow);
        PasteModeText.Text = mode.AutoPaste
            ? $"Auto-Einfuegen ist fuer \"{ModeName(activeWorkflow)}\" aktiv."
            : $"\"{ModeName(activeWorkflow)}\" kopiert nur in die Zwischenablage.";
    }

    private ModeSettings GetMode(WorkflowKind kind)
    {
        if (!settings.Modes.TryGetValue(kind, out var mode))
        {
            mode = ModeSettings.CreateDefaults()[kind];
            settings.Modes[kind] = mode;
        }

        return mode;
    }

    private string ModeName(WorkflowKind kind)
    {
        var configured = GetMode(kind).Name;
        return string.IsNullOrWhiteSpace(configured) ? DefaultModeName(kind) : configured;
    }

    private static string DefaultModeName(WorkflowKind kind) => kind switch
    {
        WorkflowKind.Transcribe => "Direkt transkribieren",
        WorkflowKind.Improve => "Professionelle E-Mail",
        WorkflowKind.Calm => "Social-Media-Post",
        WorkflowKind.Emoji => "Emojis",
        _ => kind.ToString()
    };

    private static string EffectivePrompt(WorkflowKind kind, ModeSettings mode)
    {
        if (!string.IsNullOrWhiteSpace(mode.Prompt))
        {
            return mode.Prompt;
        }

        return kind switch
        {
            WorkflowKind.Improve => PromptLibrary.ProfessionalEmail,
            WorkflowKind.Calm => PromptLibrary.SocialMediaPost,
            WorkflowKind.Emoji => PromptLibrary.Emoji,
            _ => ""
        };
    }

    private static string EffectiveLanguage(ModeSettings mode)
    {
        return string.IsNullOrWhiteSpace(mode.Language) ? "de" : mode.Language.Trim();
    }

    private static double ClampTemperature(double value)
    {
        return Math.Min(1, Math.Max(0, value));
    }

    private static string HotkeyName(int? virtualKey)
    {
        if (virtualKey is null)
        {
            return "Standard";
        }

        var key = KeyInterop.KeyFromVirtualKey(virtualKey.Value);
        return key == Key.None ? $"VK {virtualKey}" : key.ToString();
    }

    private void SetStatus(string text, AppVisualState state)
    {
        StatusText.Text = text;
        var colors = state switch
        {
            AppVisualState.Recording => (Background: "#FEE2E2", Border: "#FCA5A5", Text: "#991B1B"),
            AppVisualState.Processing => (Background: "#FEF3C7", Border: "#FCD34D", Text: "#92400E"),
            AppVisualState.Success => (Background: "#DCFCE7", Border: "#86EFAC", Text: "#166534"),
            AppVisualState.Error => (Background: "#FEE2E2", Border: "#FCA5A5", Text: "#991B1B"),
            _ => (Background: "#EAF2FF", Border: "#BBD2F6", Text: "#25456F")
        };

        StatusBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors.Background));
        StatusBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors.Border));
        StatusText.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colors.Text));
        StatusDot.Fill = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(state switch
        {
            AppVisualState.Recording => "#EF4444",
            AppVisualState.Processing => "#F59E0B",
            AppVisualState.Success => "#22C55E",
            AppVisualState.Error => "#EF4444",
            _ => "#22C55E"
        }));
        ghostWindow?.SetState(state);

        if (notifyIcon is not null)
        {
            notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        }
    }

    private void PlaySound(AppSound sound)
    {
        if (!settings.PlaySounds)
        {
            return;
        }

        switch (sound)
        {
            case AppSound.Start:
                SystemSounds.Beep.Play();
                break;
            case AppSound.Done:
                SystemSounds.Asterisk.Play();
                break;
            case AppSound.Error:
                SystemSounds.Exclamation.Play();
                break;
        }
    }

    private static string FriendlyError(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("API Key", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("401", StringComparison.OrdinalIgnoreCase))
        {
            return "API Key ungueltig oder fehlt. Bitte pruefe den gespeicherten OpenAI API Key.";
        }

        if (message.Contains("microphone", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Mikrofon", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Aufnahme", StringComparison.OrdinalIgnoreCase))
        {
            return "Mikrofon konnte nicht verwendet werden. Bitte pruefe, ob Windows den Mikrofonzugriff erlaubt und kein anderes Programm es blockiert.";
        }

        if (message.Contains("model", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Zugriff", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("access", StringComparison.OrdinalIgnoreCase))
        {
            return "Das OpenAI-Modell ist fuer deinen API Key nicht verfuegbar. Blitztext versucht bei Transkription automatisch einen Fallback.";
        }

        if (message.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("host", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("internet", StringComparison.OrdinalIgnoreCase))
        {
            return "Keine Verbindung zur OpenAI API. Bitte pruefe Internet/VPN/Firewall und versuche es erneut.";
        }

        return message;
    }

    private void SaveSettings()
    {
        settings.EnsureDefaults();
        settingsStore.Save(settings);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (System.Windows.Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown && !disposed)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void Dispose()
    {
        disposed = true;
        hotkeys.Dispose();
        recorder.Dispose();
        notifyIcon?.Dispose();
        ghostWindow?.Close();
    }
}

public enum AppVisualState
{
    Ready,
    Recording,
    Processing,
    Success,
    Error
}

internal enum AppSound
{
    Start,
    Done,
    Error
}
