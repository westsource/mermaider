using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Mermaider.Services;

public class AppSettings
{
    public double EditorPreviewRatio { get; set; } = 0.5;
    public double PreviewZoom { get; set; } = 1.0;
    public string LastOpenDirectory { get; set; } = string.Empty;
    public List<string> RecentFiles { get; set; } = new();
}

public class SettingsService
{
    public const int MaxRecentFiles = 10;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Mermaider",
        "settings.json"
    );

    public AppSettings Settings { get; private set; }

    public SettingsService()
    {
        Settings = Load();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // ignored
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(Settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // ignored
        }
    }

    public void AddRecentFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

        Settings.RecentFiles.Remove(filePath);
        Settings.RecentFiles.Insert(0, filePath);

        if (Settings.RecentFiles.Count > MaxRecentFiles)
        {
            Settings.RecentFiles = Settings.RecentFiles.Take(MaxRecentFiles).ToList();
        }

        Save();
    }

    public void RemoveRecentFile(string filePath)
    {
        if (Settings.RecentFiles.Remove(filePath))
        {
            Save();
        }
    }

    public void CleanInvalidRecentFiles()
    {
        var validFiles = Settings.RecentFiles.Where(File.Exists).ToList();
        if (validFiles.Count != Settings.RecentFiles.Count)
        {
            Settings.RecentFiles = validFiles;
            Save();
        }
    }
}
