using System;
using System.IO;
using System.Text.Json;

namespace 币安量化机器人.Services;

public class AppSettings
{
    public bool AutoReconnect { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public string Environment { get; set; } = "Production";
    public int RefreshIntervalSeconds { get; set; } = 5;
    public string LogLevel { get; set; } = "Info";
    public string? TelegramBotToken { get; set; }
    public string? TelegramChatId { get; set; }
    public string? DingTalkWebhook { get; set; }
}

public static class AppSettingsService
{
    private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "Data", "appsettings.json");
    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (settings is not null)
                    Current = settings;
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
