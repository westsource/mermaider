using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mermaider.Models;
using Mermaider.Services;
using Mermaider.Services.AIService;

namespace Mermaider.ViewModels;

public partial class AIPanelViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly AIConversationService _conversationService;
    private IAIService? _aiService;
    private string? _currentFilePath;

    [ObservableProperty]
    private ObservableCollection<AIMessage> _messages = new();

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private double _panelHeight = 200;

    [ObservableProperty]
    private string _statusMessage = "AI 助手就绪";

    [ObservableProperty]
    private bool _isConfigured;

    [ObservableProperty]
    private ObservableCollection<AIModelConfig> _availableModels = new();

    [ObservableProperty]
    private AIModelConfig? _selectedModel;

    public string ToggleButtonText => IsExpanded ? "AI 助手 ▼" : "AI 助手 ▲";

    public bool HasMessages => Messages.Count > 0;

    public bool HasModels => AvailableModels.Count > 0;

    public event EventHandler<string>? CodeGenerated;
    public event EventHandler? ToggleRequested;
    public event EventHandler? OpenSettingsRequested;

    public AIPanelViewModel(SettingsService settingsService, AIConversationService conversationService)
    {
        _settingsService = settingsService;
        _conversationService = conversationService;

        IsExpanded = settingsService.Settings.AIPanelExpanded;
        PanelHeight = settingsService.Settings.AIPanelHeight;

        LoadAvailableModels();
        InitializeAIService();
    }

    private void LoadAvailableModels()
    {
        _settingsService.ReloadSecureValues();
        
        AvailableModels.Clear();
        
        foreach (var model in _settingsService.Settings.ModelConfigs.Where(m => m.IsEnabled))
        {
            AvailableModels.Add(model);
        }

        var selectedId = _settingsService.Settings.SelectedModelId;
        SelectedModel = AvailableModels.FirstOrDefault(m => m.Id == selectedId) ?? AvailableModels.FirstOrDefault();

        OnPropertyChanged(nameof(HasModels));
    }

    private void InitializeAIService()
    {
        if (SelectedModel == null)
        {
            IsConfigured = false;
            StatusMessage = "请添加 AI 模型配置";
            return;
        }

        _aiService = CreateAIService(SelectedModel);
        IsConfigured = _aiService?.IsConfigured ?? false;

        if (!IsConfigured)
        {
            StatusMessage = SelectedModel.Provider switch
            {
                AIProvider.OpenAI when string.IsNullOrWhiteSpace(SelectedModel.ApiKey) => "请配置 API Key",
                AIProvider.AzureOpenAI when string.IsNullOrWhiteSpace(SelectedModel.ApiKey) => "请配置 Azure API Key",
                AIProvider.Ollama => "请确保 Ollama 服务已启动",
                AIProvider.Custom when string.IsNullOrWhiteSpace(SelectedModel.BaseUrl) => "请配置 Base URL",
                _ => "请完善模型配置"
            };
        }
        else
        {
            StatusMessage = $"{SelectedModel.DisplayName} 已就绪";
        }
    }

    private IAIService? CreateAIService(AIModelConfig config)
    {
        return config.Provider switch
        {
            AIProvider.OpenAI => new OpenAIService(ConvertToProviderConfig(config)),
            AIProvider.AzureOpenAI => new AzureOpenAIService(ConvertToProviderConfig(config)),
            AIProvider.Ollama => new OllamaService(ConvertToProviderConfig(config)),
            AIProvider.Custom => new CustomAIService(config),
            _ => null
        };
    }

    private AIProviderConfig ConvertToProviderConfig(AIModelConfig modelConfig)
    {
        return new AIProviderConfig
        {
            Provider = modelConfig.Provider,
            ApiKey = modelConfig.ApiKey,
            Endpoint = modelConfig.Endpoint,
            DeploymentName = modelConfig.DeploymentName,
            Model = modelConfig.ModelId,
            BaseUrl = modelConfig.BaseUrl,
            MaxTokens = modelConfig.MaxTokens,
            Temperature = modelConfig.Temperature
        };
    }

    partial void OnSelectedModelChanged(AIModelConfig? value)
    {
        if (value != null)
        {
            _settingsService.SetSelectedModel(value.Id);
            InitializeAIService();
        }
    }

    public void SetCurrentFile(string? filePath)
    {
        _currentFilePath = filePath;
        LoadConversation();
    }

    private void LoadConversation()
    {
        Messages.Clear();

        var conversation = _conversationService.GetOrCreateConversation(_currentFilePath);
        foreach (var message in conversation.Messages)
        {
            Messages.Add(message);
        }

        OnPropertyChanged(nameof(HasMessages));
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        OnPropertyChanged(nameof(ToggleButtonText));
        ToggleRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task Send()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsLoading || _aiService == null)
            return;

        var userMessage = new AIMessage
        {
            Role = MessageRole.User,
            Content = InputText.Trim(),
            Timestamp = DateTime.Now
        };

        Messages.Add(userMessage);
        OnPropertyChanged(nameof(HasMessages));

        var prompt = InputText.Trim();
        InputText = string.Empty;

        var loadingMessage = new AIMessage
        {
            Role = MessageRole.Assistant,
            Content = string.Empty,
            IsLoading = true,
            Timestamp = DateTime.Now
        };
        Messages.Add(loadingMessage);

        IsLoading = true;
        StatusMessage = "正在生成...";

        try
        {
            var currentCode = GetCurrentCode?.Invoke() ?? string.Empty;
            var history = Messages
                .Where(m => !m.IsLoading && m != loadingMessage)
                .ToList();

            var response = await _aiService.GenerateAsync(prompt, currentCode, history);

            var index = Messages.IndexOf(loadingMessage);
            if (index >= 0)
            {
                Messages[index] = response;
            }

            SaveConversation();

            if (!string.IsNullOrEmpty(response.GeneratedCode))
            {
                StatusMessage = "代码已生成";
            }
            else if (!string.IsNullOrEmpty(response.ErrorMessage))
            {
                StatusMessage = $"错误: {response.ErrorMessage}";
            }
            else
            {
                StatusMessage = "AI 助手已响应";
            }
        }
        catch (Exception ex)
        {
            var index = Messages.IndexOf(loadingMessage);
            if (index >= 0)
            {
                Messages[index] = new AIMessage
                {
                    Role = MessageRole.Assistant,
                    Content = string.Empty,
                    ErrorMessage = $"发生错误: {ex.Message}",
                    IsLoading = false,
                    Timestamp = DateTime.Now
                };
            }
            StatusMessage = $"错误: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ApplyCode(AIMessage? message)
    {
        if (message == null || string.IsNullOrEmpty(message.GeneratedCode))
            return;

        CodeGenerated?.Invoke(this, message.GeneratedCode);
        StatusMessage = "代码已应用";
    }

    [RelayCommand]
    private void RevertCode(AIMessage? message)
    {
        if (message == null || string.IsNullOrEmpty(message.CodeBeforeGeneration))
            return;

        CodeGenerated?.Invoke(this, message.CodeBeforeGeneration);
        StatusMessage = "代码已回退";
    }

    [RelayCommand]
    private void ClearHistory()
    {
        _conversationService.ClearConversation(_currentFilePath);
        Messages.Clear();
        OnPropertyChanged(nameof(HasMessages));
        StatusMessage = "对话历史已清空";
    }

    [RelayCommand]
    private void OpenSettings()
    {
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SaveConversation()
    {
        var conversation = _conversationService.GetOrCreateConversation(_currentFilePath);
        conversation.Messages = Messages.ToList();
        _conversationService.SaveConversation(conversation);
    }

    public void SaveSettings()
    {
        _settingsService.Settings.AIPanelExpanded = IsExpanded;
        _settingsService.Settings.AIPanelHeight = PanelHeight;
        _settingsService.Save();
    }

    public void RefreshConfiguration()
    {
        LoadAvailableModels();
        InitializeAIService();
    }

    public Func<string?>? GetCurrentCode { get; set; }

    partial void OnIsExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(ToggleButtonText));
    }

    partial void OnPanelHeightChanged(double value)
    {
        _settingsService.Settings.AIPanelHeight = value;
    }
}
