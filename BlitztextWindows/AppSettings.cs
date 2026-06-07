using System.IO;
using System.Text.Json;

namespace BlitztextWindows;

public sealed class AppSettings
{
    public bool AutoPaste { get; set; } = true;
    public bool PlaySounds { get; set; } = true;
    public bool StartInGhostMode { get; set; }
    public HotkeyBindings Hotkeys { get; set; } = new();
    public Dictionary<WorkflowKind, ModeSettings> Modes { get; set; } = ModeSettings.CreateDefaults();

    public void EnsureDefaults()
    {
        Modes ??= ModeSettings.CreateDefaults();
        foreach (var defaultMode in ModeSettings.CreateDefaults())
        {
            if (!Modes.ContainsKey(defaultMode.Key))
            {
                Modes[defaultMode.Key] = defaultMode.Value;
            }
        }

        Hotkeys ??= new HotkeyBindings();
    }
}

public sealed class HotkeyBindings
{
    public int? Transcribe { get; set; }
    public int? Improve { get; set; }
    public int? Calm { get; set; }
    public int? Ghost { get; set; }
}

public sealed class ModeSettings
{
    public string Name { get; set; } = "";
    public string Prompt { get; set; } = "";
    public double Temperature { get; set; }
    public bool AutoPaste { get; set; }
    public string Language { get; set; } = "de";

    public static Dictionary<WorkflowKind, ModeSettings> CreateDefaults() => new()
    {
        [WorkflowKind.Transcribe] = new ModeSettings
        {
            Name = "Direkt transkribieren",
            Prompt = "",
            Temperature = 0,
            AutoPaste = true,
            Language = "de"
        },
        [WorkflowKind.Improve] = new ModeSettings
        {
            Name = "Professionelle E-Mail",
            Prompt = PromptLibrary.ProfessionalEmail,
            Temperature = 0.2,
            AutoPaste = false,
            Language = "de"
        },
        [WorkflowKind.Calm] = new ModeSettings
        {
            Name = "Social-Media-Post",
            Prompt = PromptLibrary.SocialMediaPost,
            Temperature = 0.35,
            AutoPaste = false,
            Language = "de"
        }
    };
}

public sealed class SettingsStore
{
    private readonly string settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Blitztext",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(settingsPath)) ?? new AppSettings();
            settings.EnsureDefaults();
            return settings;
        }
        catch
        {
            var settings = new AppSettings();
            settings.EnsureDefaults();
            return settings;
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        File.WriteAllText(settingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
