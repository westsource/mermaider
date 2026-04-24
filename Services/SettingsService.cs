using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mermaider.Models;

namespace Mermaider.Services;

public class AppSettings
{
    public double EditorPreviewRatio { get; set; } = 0.5;
    public double PreviewZoom { get; set; } = 1.0;
    public string LastOpenDirectory { get; set; } = string.Empty;
    public List<string> RecentFiles { get; set; } = new();

    public List<AIModelConfig> ModelConfigs { get; set; } = new();
    public string? SelectedModelId { get; set; }

    public string? ConversationStoragePath { get; set; }
    public bool AIPanelExpanded { get; set; }
    public double AIPanelHeight { get; set; } = 200;

    public AIProvider SelectedProvider { get; set; } = AIProvider.OpenAI;
    public AIProviderConfig OpenAIConfig { get; set; } = new() { Provider = AIProvider.OpenAI, Model = "gpt-4o" };
    public AIProviderConfig AzureOpenAIConfig { get; set; } = new() { Provider = AIProvider.AzureOpenAI, Model = "gpt-4" };
    public AIProviderConfig OllamaConfig { get; set; } = new() { Provider = AIProvider.Ollama, Model = "llama3", BaseUrl = "http://localhost:11434" };
}

public class SettingsService
{
    public const int MaxRecentFiles = 10;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Mermaider",
        "settings.json"
    );

    private static readonly string SecureConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Mermaider",
        "secure.config"
    );

    public AppSettings Settings { get; private set; }

    public SettingsService()
    {
        Settings = Load();
        LoadSecureValues();
        EnsureDefaultModels();
    }

    private void EnsureDefaultModels()
    {
        if (Settings.ModelConfigs.Count == 0)
        {
            Settings.ModelConfigs = new List<AIModelConfig>
            {
                new AIModelConfig
                {
                    Id = "openai-default",
                    Name = "OpenAI GPT-4o",
                    Provider = AIProvider.OpenAI,
                    ModelId = "gpt-4o",
                    IsEnabled = true
                },
                new AIModelConfig
                {
                    Id = "ollama-default",
                    Name = "Ollama (本地)",
                    Provider = AIProvider.Ollama,
                    ModelId = "llama3",
                    BaseUrl = "http://localhost:11434",
                    IsEnabled = true
                }
            };
            Settings.SelectedModelId = "openai-default";
            Save();
        }

        if (string.IsNullOrEmpty(Settings.SelectedModelId) && Settings.ModelConfigs.Count > 0)
        {
            Settings.SelectedModelId = Settings.ModelConfigs[0].Id;
        }
    }

    private void LoadSecureValues()
    {
        foreach (var config in Settings.ModelConfigs)
        {
            config.ApiKey = SecureStorageService.LoadProtectedValue($"Model_{config.Id}_ApiKey", SecureConfigPath);
        }
        Settings.OpenAIConfig.ApiKey = SecureStorageService.LoadProtectedValue("OpenAI_ApiKey", SecureConfigPath);
        Settings.AzureOpenAIConfig.ApiKey = SecureStorageService.LoadProtectedValue("Azure_ApiKey", SecureConfigPath);
    }

    public void ReloadSecureValues()
    {
        LoadSecureValues();
    }

    public void SaveModelApiKey(string modelId, string? apiKey)
    {
        var config = Settings.ModelConfigs.FirstOrDefault(c => c.Id == modelId);
        if (config != null)
        {
            config.ApiKey = apiKey;
            SecureStorageService.SaveProtectedValue($"Model_{modelId}_ApiKey", apiKey, SecureConfigPath);
        }
    }

    public AIModelConfig? GetSelectedModelConfig()
    {
        return Settings.ModelConfigs.FirstOrDefault(c => c.Id == Settings.SelectedModelId);
    }

    public AIModelConfig? GetModelConfig(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return null;
        return Settings.ModelConfigs.FirstOrDefault(c => c.Id == modelId);
    }

    public void AddModelConfig(AIModelConfig config)
    {
        config.Id = string.IsNullOrEmpty(config.Id) ? Guid.NewGuid().ToString() : config.Id;
        Settings.ModelConfigs.Add(config);
        Save();
    }

    public void UpdateModelConfig(AIModelConfig config)
    {
        var existing = Settings.ModelConfigs.FirstOrDefault(c => c.Id == config.Id);
        if (existing != null)
        {
            var index = Settings.ModelConfigs.IndexOf(existing);
            Settings.ModelConfigs[index] = config;
            Save();
        }
    }

    public void RemoveModelConfig(string modelId)
    {
        var config = Settings.ModelConfigs.FirstOrDefault(c => c.Id == modelId);
        if (config != null)
        {
            Settings.ModelConfigs.Remove(config);
            SecureStorageService.SaveProtectedValue($"Model_{modelId}_ApiKey", null, SecureConfigPath);
            if (Settings.SelectedModelId == modelId)
            {
                Settings.SelectedModelId = Settings.ModelConfigs.FirstOrDefault()?.Id;
            }
            Save();
        }
    }

    public void SetSelectedModel(string? modelId)
    {
        if (Settings.ModelConfigs.Any(c => c.Id == modelId))
        {
            Settings.SelectedModelId = modelId;
            Save();
        }
    }

    public void SaveApiKey(AIProvider provider, string? apiKey)
    {
        switch (provider)
        {
            case AIProvider.OpenAI:
                Settings.OpenAIConfig.ApiKey = apiKey;
                SecureStorageService.SaveProtectedValue("OpenAI_ApiKey", apiKey, SecureConfigPath);
                break;
            case AIProvider.AzureOpenAI:
                Settings.AzureOpenAIConfig.ApiKey = apiKey;
                SecureStorageService.SaveProtectedValue("Azure_ApiKey", apiKey, SecureConfigPath);
                break;
            case AIProvider.Ollama:
            case AIProvider.Custom:
                break;
        }
    }

    public AIProviderConfig GetCurrentProviderConfig()
    {
        return Settings.SelectedProvider switch
        {
            AIProvider.OpenAI => Settings.OpenAIConfig,
            AIProvider.AzureOpenAI => Settings.AzureOpenAIConfig,
            AIProvider.Ollama => Settings.OllamaConfig,
            _ => Settings.OpenAIConfig
        };
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
