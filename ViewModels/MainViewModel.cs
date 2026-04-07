using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Input;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Highlighting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mermaider.Models;
using Mermaider.Services;
using Mermaider.Views;
using Window = Avalonia.Controls.Window;

namespace Mermaider.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MermaidService _mermaidService;
    private readonly FileService _fileService;
    private readonly SettingsService _settingsService;
    private readonly IStorageProvider _storageProvider;
    private readonly Window _ownerWindow;
    private Timer? _debounceTimer;
    private readonly object _timerLock = new();

    public static readonly IValueConverter TabBackgroundConverter = new FuncValueConverter<bool, IBrush>(
        isSelected => isSelected ? new SolidColorBrush(Color.Parse("#FFFFFF")) : new SolidColorBrush(Color.Parse("#E8E8E8"))
    );

    public static readonly IValueConverter TabFontWeightConverter = new FuncValueConverter<bool, FontWeight>(
        isSelected => isSelected ? FontWeight.Bold : FontWeight.Normal
    );

    [ObservableProperty]
    private ObservableCollection<TabItem> _tabs = new();

    [ObservableProperty]
    private int _selectedTabIndex = -1;

    [ObservableProperty]
    private double _previewZoom = 1.0;

    [ObservableProperty]
    private double _previewFitScale = 1.0;

    [ObservableProperty]
    private double _editorPreviewRatio = 0.5;

    [ObservableProperty]
    private double _editorPanelWidth = 640;

    [ObservableProperty]
    private bool _isRendering;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;
    private const double ZoomStep = 0.1;

    [ObservableProperty]
    private bool _canUndoAction;

    [ObservableProperty]
    private bool _canRedoAction;

    [ObservableProperty]
    private bool _canCutAction;

    [ObservableProperty]
    private bool _canCopyAction;

    [ObservableProperty]
    private bool _canPasteAction = true;

    [ObservableProperty]
    private bool _canSelectAllAction;

    [ObservableProperty]
    private ObservableCollection<RecentFileItem> _recentFiles = new();

    public bool HasRecentFiles => RecentFiles.Count > 0;

    public string ZoomText => $"缩放: {(int)(PreviewDisplayScale * 100)}%";

    public double PreviewDisplayScale => PreviewZoom * PreviewFitScale;

    public IHighlightingDefinition MermaidHighlighting { get; } = MermaidHighlightingProvider.Create();

    public TabItem? CurrentTab => SelectedTabIndex >= 0 && SelectedTabIndex < Tabs.Count ? Tabs[SelectedTabIndex] : null;

    partial void OnPreviewZoomChanged(double value)
    {
        OnPropertyChanged(nameof(PreviewDisplayScale));
        OnPropertyChanged(nameof(ZoomText));
    }

    partial void OnPreviewFitScaleChanged(double value)
    {
        OnPropertyChanged(nameof(PreviewDisplayScale));
        OnPropertyChanged(nameof(ZoomText));
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        for (int i = 0; i < Tabs.Count; i++)
        {
            Tabs[i].IsSelected = (i == value);
        }

        PreviewFitScale = 1.0;
        ResetEditorState();
        OnPropertyChanged(nameof(PreviewDisplayScale));
        OnPropertyChanged(nameof(ZoomText));
        OnPropertyChanged(nameof(CurrentTab));
    }

    public MainViewModel(MermaidService mermaidService, FileService fileService, SettingsService settingsService, IStorageProvider storageProvider, Window ownerWindow)
    {
        _mermaidService = mermaidService;
        _fileService = fileService;
        _settingsService = settingsService;
        _storageProvider = storageProvider;
        _ownerWindow = ownerWindow;
        _fileService.SetStorageProvider(storageProvider);

        EditorPreviewRatio = settingsService.Settings.EditorPreviewRatio;
        PreviewZoom = settingsService.Settings.PreviewZoom;

        _settingsService.CleanInvalidRecentFiles();
        foreach (var file in settingsService.Settings.RecentFiles)
        {
            RecentFiles.Add(new RecentFileItem(file));
        }

        AddNewTab();
    }

    public void SaveSettings()
    {
        _settingsService.Settings.EditorPreviewRatio = EditorPreviewRatio;
        _settingsService.Settings.PreviewZoom = PreviewZoom;
        _settingsService.Save();
    }

    [RelayCommand]
    private void AddNewTab()
    {
        var tab = new TabItem
        {
            Header = "未命名.mmd",
            Content = GetDefaultMermaidCode()
        };
        tab.ContentChanged += OnTabContentChanged;
        Tabs.Add(tab);
        SelectTab(Tabs.Count - 1, forceNotify: true);
        ScheduleValidationAndRender(tab);
    }

    private void SelectTab(int index, bool forceNotify = false)
    {
        var previousIndex = SelectedTabIndex;
        SelectedTabIndex = index;

        if (forceNotify && previousIndex == index)
        {
            for (int i = 0; i < Tabs.Count; i++)
            {
                Tabs[i].IsSelected = (i == index);
            }

            OnPropertyChanged(nameof(CurrentTab));
        }
    }

    private void OnTabContentChanged(object? sender, EventArgs e)
    {
        if (sender is TabItem tab)
        {
            tab.IsModified = true;
            tab.UpdateHeader();
            ScheduleValidationAndRender(tab);
        }
    }

    private void ScheduleValidationAndRender(TabItem tab)
    {
        lock (_timerLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(async _ =>
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await ValidateAndRenderTab(tab);
            });
        }, null, TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task ValidateAndRenderTab(TabItem tab)
    {
        if (string.IsNullOrWhiteSpace(tab.Content))
        {
            tab.PreviewImage = null;
            tab.HasError = false;
            tab.ErrorMessage = null;
            StatusMessage = "就绪";
            return;
        }

        StatusMessage = "正在检查语法...";
        IsRendering = true;

        try
        {
            var result = await _mermaidService.ValidateSyntaxAsync(tab.Content);
            
            if (result.Success)
            {
                StatusMessage = "语法正确，正在渲染...";
                tab.HasError = false;
                tab.ErrorMessage = null;

                var imageData = await _mermaidService.RenderToPngAsync(tab.Content, 3.0);
                if (imageData != null)
                {
                    using var stream = new MemoryStream(imageData);
                    tab.PreviewImage = new Bitmap(stream);
                    StatusMessage = "渲染完成";
                }
            }
            else
            {
                tab.HasError = true;
                tab.ErrorMessage = result.Error;
                tab.PreviewImage = null;
                StatusMessage = $"语法错误: {(result.Error?.Length > 50 ? result.Error.Substring(0, 50) + "..." : result.Error)}";
            }
        }
        catch (Exception ex)
        {
            tab.HasError = true;
            tab.ErrorMessage = ex.Message;
            tab.PreviewImage = null;
            StatusMessage = $"错误: {ex.Message}";
        }
        finally
        {
            IsRendering = false;
        }
    }

    private async Task<bool> SaveTabAsync(TabItem tab)
    {
        if (string.IsNullOrEmpty(tab.FilePath))
        {
            var filePath = await _fileService.SaveFileAsync(tab.Content, tab.Header);
            if (filePath == null)
            {
                return false;
            }

            tab.FilePath = filePath;
        }
        else
        {
            await _fileService.SaveFileToPathAsync(tab.Content, tab.FilePath);
        }

        tab.IsModified = false;
        tab.UpdateHeader();
        StatusMessage = "已保存";
        return true;
    }

    private async Task<SaveChangesDialogResult> ShowSaveChangesDialogAsync(TabItem tab)
    {
        var dialog = new SaveChangesDialog(tab.Header);
        return await dialog.ShowDialog<SaveChangesDialogResult>(_ownerWindow);
    }

    private async Task<bool> ConfirmCloseTabAsync(TabItem tab)
    {
        if (!tab.IsModified)
        {
            return true;
        }

        var action = await ShowSaveChangesDialogAsync(tab);
        return action switch
        {
            SaveChangesDialogResult.Save => await SaveTabAsync(tab),
            SaveChangesDialogResult.DontSave => true,
            _ => false
        };
    }

    [RelayCommand]
    private async Task CloseTab(TabItem tab)
    {
        var index = Tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        if (!await ConfirmCloseTabAsync(tab))
        {
            StatusMessage = "已取消关闭";
            return;
        }

        tab.ContentChanged -= OnTabContentChanged;

        var wasSelected = (index == SelectedTabIndex);

        Tabs.RemoveAt(index);

        if (Tabs.Count == 0)
        {
            SelectedTabIndex = -1;
            OnPropertyChanged(nameof(CurrentTab));

            // 关闭最后一个标签页后立即创建新的示例标签页
            AddNewTab();
        }
        else if (wasSelected)
        {
            // 如果关闭的是当前选中的标签，选择相邻的标签
            var newIndex = Math.Min(index, Tabs.Count - 1);
            SelectTab(newIndex, forceNotify: true);
        }
        else if (SelectedTabIndex > index)
        {
            // 如果关闭的标签在当前选中标签之前，调整索引
            SelectedTabIndex--;
        }
        else
        {
            // 即使不是当前选中的标签被关闭，也需要更新 IsSelected 状态
            for (int i = 0; i < Tabs.Count; i++)
            {
                Tabs[i].IsSelected = (i == SelectedTabIndex);
            }
        }
    }

    [RelayCommand]
    private async Task NewFile()
    {
        AddNewTab();
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        var (content, filePath) = await _fileService.OpenFileAsync();
        if (content != null)
        {
            var tab = new TabItem
            {
                Content = content,
                FilePath = filePath
            };
            tab.ContentChanged += OnTabContentChanged;
            tab.UpdateHeader();
            Tabs.Add(tab);
            SelectedTabIndex = Tabs.Count - 1;
            await ValidateAndRenderTab(tab);

            if (!string.IsNullOrEmpty(filePath))
            {
                AddToRecentFiles(filePath);
            }
        }
    }

    public async Task OpenFileFromPath(string filePath)
    {
        var content = await _fileService.OpenFileFromPathAsync(filePath);
        if (content != null)
        {
            var tab = new TabItem
            {
                Content = content,
                FilePath = filePath
            };
            tab.ContentChanged += OnTabContentChanged;
            tab.UpdateHeader();
            Tabs.Add(tab);
            SelectedTabIndex = Tabs.Count - 1;
            await ValidateAndRenderTab(tab);

            AddToRecentFiles(filePath);
        }
    }

    [RelayCommand]
    private async Task OpenRecentFile(RecentFileItem? item)
    {
        if (item == null) return;

        var filePath = item.FilePath;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            RemoveFromRecentFiles(filePath);
            StatusMessage = "文件不存在或已被删除";
            return;
        }

        var existingTab = Tabs.FirstOrDefault(t => t.FilePath == filePath);
        if (existingTab != null)
        {
            SelectedTabIndex = Tabs.IndexOf(existingTab);
            return;
        }

        await OpenFileFromPath(filePath);
    }

    private void AddToRecentFiles(string filePath)
    {
        var existing = RecentFiles.FirstOrDefault(r => r.FilePath == filePath);
        if (existing != null)
        {
            RecentFiles.Remove(existing);
        }

        var item = new RecentFileItem(filePath);
        RecentFiles.Insert(0, item);

        const int maxRecentFiles = 10;
        while (RecentFiles.Count > maxRecentFiles)
        {
            RecentFiles.RemoveAt(RecentFiles.Count - 1);
        }

        _settingsService.AddRecentFile(filePath);
        OnPropertyChanged(nameof(HasRecentFiles));
    }

    private void RemoveFromRecentFiles(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        var item = RecentFiles.FirstOrDefault(r => r.FilePath == filePath);
        if (item != null)
        {
            RecentFiles.Remove(item);
            _settingsService.RemoveRecentFile(filePath);
            OnPropertyChanged(nameof(HasRecentFiles));
        }
    }

    [RelayCommand]
    private async Task SaveFile()
    {
        if (CurrentTab == null) return;
        await SaveTabAsync(CurrentTab);
    }

    [RelayCommand]
    private async Task SaveFileAs()
    {
        if (CurrentTab == null) return;
        var filePath = await _fileService.SaveFileAsync(CurrentTab.Content, CurrentTab.Header);
        if (filePath == null) return;

        CurrentTab.FilePath = filePath;
        CurrentTab.IsModified = false;
        CurrentTab.UpdateHeader();
        StatusMessage = "已保存";
    }

    [RelayCommand]
    private async Task CopyImage()
    {
        if (CurrentTab == null) return;

        if (CurrentTab.PreviewImage == null)
        {
            await ValidateAndRenderTab(CurrentTab);
        }

        if (CurrentTab.PreviewImage == null) return;

        using var stream = new MemoryStream();
        CurrentTab.PreviewImage.Save(stream);
        var pngBytes = stream.ToArray();

        var clipboard = _ownerWindow.Clipboard;
        if (clipboard == null)
        {
            StatusMessage = "当前环境不支持剪贴板";
            return;
        }

        var dataObject = new DataObject();
        dataObject.Set("image/png", pngBytes);
        dataObject.Set("PNG", pngBytes);

        await clipboard.SetDataObjectAsync(dataObject);
        StatusMessage = "图片已复制到剪贴板";
    }

    [RelayCommand]
    private async Task SaveImage()
    {
        if (CurrentTab == null) return;

        if (CurrentTab.PreviewImage == null)
        {
            await ValidateAndRenderTab(CurrentTab);
        }

        if (CurrentTab.PreviewImage == null) return;

        using var stream = new MemoryStream();
        CurrentTab.PreviewImage.Save(stream);
        var imageData = stream.ToArray();

        var fileName = Path.GetFileNameWithoutExtension(CurrentTab.Header) + ".png";
        var result = await _fileService.SaveImageAsync(imageData, fileName);
        if (result != null)
        {
            StatusMessage = "图片已保存";
        }
    }

    [RelayCommand]
    private void ZoomIn()
    {
        PreviewZoom = Math.Min(PreviewZoom + ZoomStep, MaxZoom);
        StatusMessage = $"缩放: {(int)(PreviewZoom * 100)}%";
    }

    [RelayCommand]
    private void ZoomOut()
    {
        PreviewZoom = Math.Max(PreviewZoom - ZoomStep, MinZoom);
        StatusMessage = $"缩放: {(int)(PreviewZoom * 100)}%";
    }

    [RelayCommand]
    private void ResetZoom()
    {
        PreviewZoom = 1.0;
        StatusMessage = "缩放已重置";
    }

    [RelayCommand]
    private async Task CloseCurrentTab()
    {
        if (CurrentTab != null)
        {
            await CloseTab(CurrentTab);
        }
    }

    [RelayCommand]
    private void Exit()
    {
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    [RelayCommand]
    private void Undo()
    {
        if (_ownerWindow is MainWindow mainWindow)
        {
            mainWindow.UndoEditor();
        }
    }

    [RelayCommand]
    private void Redo()
    {
        if (_ownerWindow is MainWindow mainWindow)
        {
            mainWindow.RedoEditor();
        }
    }

    [RelayCommand]
    private void Cut()
    {
        if (_ownerWindow is MainWindow mainWindow)
        {
            mainWindow.CutEditor();
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (_ownerWindow is MainWindow mainWindow)
        {
            mainWindow.CopyEditor();
        }
    }

    [RelayCommand]
    private void Paste()
    {
        if (_ownerWindow is MainWindow mainWindow)
        {
            mainWindow.PasteEditor();
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        if (_ownerWindow is MainWindow mainWindow)
        {
            mainWindow.SelectAllEditor();
        }
    }

    [RelayCommand]
    private void About()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
        var dialog = new AboutDialog(
            "Mermaider",
            "本地 Mermaid 图表编辑器，支持代码编辑、语法高亮、实时预览、语法检测和图片导出。",
            "黄超（道荣）",
            version
        );

        _ = dialog.ShowDialog(_ownerWindow);
    }

    public void UpdatePreviewFitScale(Size viewportSize)
    {
        if (CurrentTab?.PreviewImage == null || viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            PreviewFitScale = 1.0;
            return;
        }

        var pixelSize = CurrentTab.PreviewImage.PixelSize;
        if (pixelSize.Width <= 0 || pixelSize.Height <= 0)
        {
            PreviewFitScale = 1.0;
            return;
        }

        const double padding = 48;
        var availableWidth = viewportSize.Width - padding;
        var availableHeight = viewportSize.Height - padding;

        if (availableWidth <= 0 || availableHeight <= 0)
        {
            PreviewFitScale = 1.0;
            return;
        }

        var horizontalScale = availableWidth / pixelSize.Width;
        var verticalScale = availableHeight / pixelSize.Height;
        PreviewFitScale = Math.Min(1.0, Math.Min(horizontalScale, verticalScale));
    }

    public void UpdateEditorState(bool canUndo, bool canRedo, bool hasSelection, bool hasText)
    {
        var hasTab = CurrentTab != null;
        CanUndoAction = hasTab && canUndo;
        CanRedoAction = hasTab && canRedo;
        CanCutAction = hasTab && hasSelection;
        CanCopyAction = hasTab && hasSelection;
        CanPasteAction = hasTab;
        CanSelectAllAction = hasTab && hasText;
    }

    public void UpdateWorkspaceLayout(double totalWidth)
    {
        if (totalWidth <= 0)
        {
            return;
        }

        const double splitterWidth = 5;
        const double minEditorWidth = 420;
        const double maxEditorWidth = 860;
        const double minPreviewWidth = 360;

        var usableWidth = Math.Max(0, totalWidth - splitterWidth);
        var targetWidth = usableWidth * EditorPreviewRatio;
        var editorWidth = Math.Clamp(targetWidth, minEditorWidth, maxEditorWidth);

        if (usableWidth - editorWidth < minPreviewWidth)
        {
            editorWidth = Math.Max(320, usableWidth - minPreviewWidth);
        }

        EditorPanelWidth = Math.Max(320, editorWidth);
    }

    private void ResetEditorState()
    {
        CanUndoAction = false;
        CanRedoAction = false;
        CanCutAction = false;
        CanCopyAction = false;
        CanPasteAction = CurrentTab != null;
        CanSelectAllAction = false;
    }

    private static string GetDefaultMermaidCode()
    {
        return @"graph TD
    A[开始] --> B{判断}
    B -->|是| C[处理A]
    B -->|否| D[处理B]
    C --> E[结束]
    D --> E";
    }
}
