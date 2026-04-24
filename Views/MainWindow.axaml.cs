using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaWebView;
using Mermaider.Models;
using Mermaider.ViewModels;
using Mermaider.Views;

namespace Mermaider.Views;

public partial class MainWindow : Window
{
    private TextEditor? _codeEditor;
    private Border? _previewWebHost;
    private MainViewModel? _viewModel;
    private Grid? _workspaceGrid;
    private Border? _splitterBorder;
    private Border? _previewGrid;
    private WebView? _previewWebViewControl;
    private MethodInfo? _webViewNavigateMethod;
    private PropertyInfo? _webViewSourceProperty;
    private PropertyInfo? _webViewUrlProperty;
    private string? _pendingPreviewHtml;
    private string? _previewTempDir;
    private string? _previewHtmlPath;
    private bool _webViewAttached;
    private bool _webViewInitTried;
    private bool _isClosing;

    private bool _isDraggingSplitter;
    private bool _splitterDragStarted;
    private double _splitterStartX;
    private double _editorStartWidth;

    private bool _isDraggingAIPanelSplitter;
    private bool _aiPanelSplitterDragStarted;
    private double _aiPanelSplitterStartY;
    private double _aiPanelStartHeight;

    private const double MinEditorWidth = 320;
    private const double MaxEditorWidth = 860;
    private const double MinPreviewWidth = 480;
    private const double MinAIPanelHeight = 50;
    private const double MaxAIPanelHeight = 600;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachPreviewHandlers();
        Closing += OnClosing;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AttachPreviewHandlers()
    {
        _codeEditor = this.FindControl<TextEditor>("CodeEditor");
        _previewWebHost = this.FindControl<Border>("PreviewWebHost");
        _workspaceGrid = this.FindControl<Grid>("WorkspaceGrid");
        _splitterBorder = this.FindControl<Border>("SplitterBorder");
        _previewGrid = this.FindControl<Border>("PreviewGrid");

        if (_codeEditor != null)
        {
            _codeEditor.TextChanged += (_, _) => RefreshEditorState();
            _codeEditor.TextArea.SelectionChanged += (_, _) => RefreshEditorState();
            _codeEditor.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == nameof(TextEditor.Document))
                {
                    Dispatcher.UIThread.Post(RefreshEditorState, DispatcherPriority.Background);
                }
            };
        }

    }

    private void ResetPreviewOffset()
    {
        // WebView 预览不再支持拖拽偏移
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Dispatcher.UIThread.Post(() =>
            {
                RefreshEditorState();
                UpdateWorkspaceLayout();
                UpdateWebPreview();
            }, DispatcherPriority.Background);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.CurrentTab) or nameof(MainViewModel.SelectedTabIndex))
        {
            Dispatcher.UIThread.Post(RefreshEditorState, DispatcherPriority.Background);
            Dispatcher.UIThread.Post(() =>
            {
                ResetPreviewOffset();
                UpdatePreviewFitScale();
                UpdateWebPreview();
            }, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentPreviewHtml))
        {
            Dispatcher.UIThread.Post(UpdateWebPreview, DispatcherPriority.Background);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SaveSettings();
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel && viewModel.HasUnsavedChanges)
        {
            e.Cancel = true;
            _isClosing = true;

            var canClose = await viewModel.ConfirmCloseAsync();
            if (canClose)
            {
                viewModel.SaveSettings();
                Close();
            }
            else
            {
                _isClosing = false;
            }
        }
    }

    private void OnSplitterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDraggingSplitter = true;
            _splitterDragStarted = false;
            _splitterStartX = e.GetPosition(this).X;
            _editorStartWidth = _viewModel?.EditorPanelWidth ?? 640;
        }
    }

    private void OnSplitterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingSplitter || _viewModel == null || _workspaceGrid == null)
            return;

        var currentX = e.GetPosition(this).X;
        var deltaX = currentX - _splitterStartX;

        if (!_splitterDragStarted)
        {
            if (Math.Abs(deltaX) < 3)
                return;
            _splitterDragStarted = true;
            e.Pointer.Capture(_splitterBorder);
        }

        var totalWidth = _workspaceGrid.Bounds.Width;
        var splitterWidth = 5;

        var newEditorWidth = _editorStartWidth + deltaX;
        var usableWidth = totalWidth - splitterWidth;

        newEditorWidth = Math.Clamp(newEditorWidth, MinEditorWidth, MaxEditorWidth);

        if (usableWidth - newEditorWidth < MinPreviewWidth)
        {
            newEditorWidth = Math.Max(MinEditorWidth, usableWidth - MinPreviewWidth);
        }

        _viewModel.EditorPanelWidth = newEditorWidth;
        _viewModel.EditorPreviewRatio = totalWidth > 0 ? newEditorWidth / totalWidth : 0.5;
        e.Handled = true;
    }

    private void OnSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingSplitter)
        {
            var wasDragStarted = _splitterDragStarted;
            _isDraggingSplitter = false;
            _splitterDragStarted = false;
            e.Pointer.Capture(null);
            if (wasDragStarted)
                e.Handled = true;
        }
    }

    private void OnToggleTriangleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel != null)
        {
            _viewModel.ToggleEditorVisibilityCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnAIPanelTogglePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel?.AiAssistant != null)
        {
            _viewModel.AiAssistant.ToggleCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnAIPanelSplitterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDraggingAIPanelSplitter = true;
            _aiPanelSplitterDragStarted = false;
            _aiPanelSplitterStartY = e.GetPosition(this).Y;
            _aiPanelStartHeight = _viewModel?.AiAssistant?.PanelHeight ?? 200;
        }
    }

    private void OnAIPanelSplitterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingAIPanelSplitter || _viewModel?.AiAssistant == null)
            return;

        var currentY = e.GetPosition(this).Y;
        var deltaY = _aiPanelSplitterStartY - currentY;

        if (!_aiPanelSplitterDragStarted)
        {
            if (Math.Abs(deltaY) < 3)
                return;
            _aiPanelSplitterDragStarted = true;
            e.Pointer.Capture(sender as Control);
        }

        var newHeight = _aiPanelStartHeight + deltaY;
        newHeight = Math.Clamp(newHeight, MinAIPanelHeight, MaxAIPanelHeight);
        _viewModel.AiAssistant.PanelHeight = newHeight;
        e.Handled = true;
    }

    private void OnAIPanelSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingAIPanelSplitter)
        {
            var wasDragStarted = _aiPanelSplitterDragStarted;
            _isDraggingAIPanelSplitter = false;
            _aiPanelSplitterDragStarted = false;
            e.Pointer.Capture(null);
            if (wasDragStarted)
                e.Handled = true;
        }
    }

    private void OnResetPreviewClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ResetZoomCommand.Execute(null);
        }

        ResetPreviewOffset();
        UpdatePreviewFitScale();
    }

    private void OnWorkspaceSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateWorkspaceLayout();
        UpdateWebPreview();
    }

    private void UpdatePreviewFitScale()
    {
        if (DataContext is not MainViewModel viewModel || _previewGrid == null)
        {
            return;
        }

        var viewportSize = _previewGrid.Bounds.Size;
        if (viewportSize.Width > 0 && viewportSize.Height > 0)
        {
            viewModel.UpdatePreviewFitScale(viewportSize);
        }
    }

    private void UpdateWorkspaceLayout()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var width = Bounds.Width;
        if (width > 0)
        {
            viewModel.UpdateWorkspaceLayout(width);
        }
    }

    private void OnTabPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border && border.DataContext is Models.TabItem tab && DataContext is MainViewModel viewModel)
        {
            var index = viewModel.Tabs.IndexOf(tab);
            if (index >= 0)
            {
                viewModel.SelectedTabIndex = index;
                Dispatcher.UIThread.Post(() =>
                {
                    ResetPreviewOffset();
                    UpdatePreviewFitScale();
                }, DispatcherPriority.Background);
            }
        }
    }

    public void UndoEditor()
    {
        ExecuteEditorAction(editor =>
        {
            if (editor.CanUndo)
            {
                editor.Undo();
            }
        });
    }

    public void RedoEditor()
    {
        ExecuteEditorAction(editor =>
        {
            if (editor.CanRedo)
            {
                editor.Redo();
            }
        });
    }

    public void CutEditor()
    {
        ExecuteEditorAction(editor => editor.Cut());
    }

    public void CopyEditor()
    {
        ExecuteEditorAction(editor => editor.Copy());
    }

    public void PasteEditor()
    {
        ExecuteEditorAction(editor => editor.Paste());
    }

    public void SelectAllEditor()
    {
        ExecuteEditorAction(editor => editor.SelectAll());
    }

    private void ExecuteEditorAction(Action<TextEditor> action)
    {
        if (_codeEditor == null)
        {
            return;
        }

        _codeEditor.Focus();
        action(_codeEditor);
        RefreshEditorState();
    }

    private void RefreshEditorState()
    {
        if (_viewModel == null || _codeEditor == null)
        {
            return;
        }

        var hasText = !string.IsNullOrEmpty(_codeEditor.Text);
        var hasSelection = _codeEditor.SelectionLength > 0;
        _viewModel.UpdateEditorState(_codeEditor.CanUndo, _codeEditor.CanRedo, hasSelection, hasText);
    }

    private void UpdateWebPreview()
    {
        if (_viewModel == null || _previewWebHost == null)
        {
            return;
        }

        var html = _viewModel.CurrentPreviewHtml;
        if (string.IsNullOrWhiteSpace(html))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_viewModel.CurrentTab?.Content))
        {
            if (_previewWebViewControl == null)
            {
                return;
            }
        }

        if (!EnsureWebViewReady())
        {
            _viewModel.StatusMessage = "WebView 初始化失败，无法显示实时预览";
            return;
        }

        if (_previewWebViewControl == null)
        {
            return;
        }

        try
        {
            _pendingPreviewHtml = html;
            TryApplyPendingWebPreview();
        }
        catch (Exception ex)
        {
            _viewModel.StatusMessage = $"WebView 预览失败: {ex.Message}";
        }
    }

    private bool EnsureWebViewReady()
    {
        if (_previewWebHost?.Child is WebView existingWebView)
        {
            _previewWebViewControl = existingWebView;
            _webViewAttached = true;
            return true;
        }

        if (_previewWebViewControl != null)
        {
            return true;
        }

        if (_previewWebHost == null)
        {
            return false;
        }

        if (_previewWebHost.Bounds.Width == 0 || _previewWebHost.Bounds.Height == 0)
        {
            // Delay initialization until the container has valid size to avoid orphaned HWND bugs in WebView2
            return false;
        }

        if (_webViewInitTried)
        {
            return false;
        }
        _webViewInitTried = true;

        try
        {
            _previewWebHost.Child = null;
            var webViewControl = new WebView();
            var webViewType = webViewControl.GetType();
            _webViewNavigateMethod = webViewType.GetMethod("Navigate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _webViewSourceProperty = webViewType.GetProperty("Source", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _webViewUrlProperty = webViewType.GetProperty("Url", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            webViewControl.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            webViewControl.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
            webViewControl.AttachedToVisualTree += (_, _) =>
            {
                _webViewAttached = true;
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(500);
                    try
                    {
                        TryApplyPendingWebPreview();
                    }
                    catch
                    {
                    }
                }, DispatcherPriority.Background);
            };
            _previewWebHost.Child = webViewControl;
            _previewWebViewControl = webViewControl;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void TryApplyPendingWebPreview()
    {
        if (_previewWebViewControl == null || string.IsNullOrWhiteSpace(_pendingPreviewHtml))
        {
            return;
        }

        if (!_webViewAttached)
        {
            return;
        }

        EnsurePreviewFilesReady();
        if (string.IsNullOrWhiteSpace(_previewHtmlPath))
        {
            throw new InvalidOperationException("预览文件初始化失败。");
        }

        File.WriteAllText(_previewHtmlPath, _pendingPreviewHtml, Encoding.UTF8);
        var previewUri = new Uri(_previewHtmlPath);
        var previewUriString = previewUri.AbsoluteUri + "?t=" + DateTime.Now.Ticks;
        var cacheBustedUri = new Uri(previewUriString);

        if (_webViewNavigateMethod != null)
        {
            var parameters = _webViewNavigateMethod.GetParameters();
            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
            {
                _webViewNavigateMethod.Invoke(_previewWebViewControl, new object[] { previewUriString });
                _pendingPreviewHtml = null;
                return;
            }

            if (parameters.Length == 1 && parameters[0].ParameterType == typeof(Uri))
            {
                _webViewNavigateMethod.Invoke(_previewWebViewControl, new object[] { cacheBustedUri });
                _pendingPreviewHtml = null;
                return;
            }
        }

        if (_webViewSourceProperty != null && _webViewSourceProperty.PropertyType == typeof(Uri))
        {
            _webViewSourceProperty.SetValue(_previewWebViewControl, cacheBustedUri);
            _pendingPreviewHtml = null;
            return;
        }

        if (_webViewSourceProperty != null && _webViewSourceProperty.PropertyType == typeof(string))
        {
            _webViewSourceProperty.SetValue(_previewWebViewControl, previewUriString);
            _pendingPreviewHtml = null;
            return;
        }

        if (_webViewUrlProperty != null && _webViewUrlProperty.PropertyType == typeof(Uri))
        {
            _webViewUrlProperty.SetValue(_previewWebViewControl, cacheBustedUri);
            _pendingPreviewHtml = null;
            return;
        }

        if (_webViewUrlProperty != null && _webViewUrlProperty.PropertyType == typeof(string))
        {
            _webViewUrlProperty.SetValue(_previewWebViewControl, previewUriString);
            _pendingPreviewHtml = null;
            return;
        }

        throw new InvalidOperationException("当前 WebView 版本不支持可用导航方式（Navigate/Source/Url）。");
    }

    private void EnsurePreviewFilesReady()
    {
        if (!string.IsNullOrWhiteSpace(_previewHtmlPath) && File.Exists(_previewHtmlPath))
        {
            return;
        }

        _previewTempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mermaider",
            "webpreview");
        Directory.CreateDirectory(_previewTempDir);

        var scriptPath = Path.Combine(_previewTempDir, "mermaid.min.js");
        if (!File.Exists(scriptPath))
        {
            var scriptUri = new Uri("avares://Mermaider/Assets/mermaid.min.js");
            using var scriptStream = AssetLoader.Open(scriptUri);
            using var fileStream = File.Create(scriptPath);
            scriptStream.CopyTo(fileStream);
        }

        _previewHtmlPath = Path.Combine(_previewTempDir, "preview.html");
    }

}
