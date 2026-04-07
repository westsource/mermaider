using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using Mermaider.Models;
using Mermaider.ViewModels;

namespace Mermaider.Views;

public partial class MainWindow : Window
{
    private TextEditor? _codeEditor;
    private Image? _previewImage;
    private MainViewModel? _viewModel;
    private Grid? _workspaceGrid;
    private Border? _splitterBorder;
    private Grid? _previewGrid;
    private Border? _previewImageBorder;
    private LayoutTransformControl? _previewTransformControl;
    private Grid? _previewContentGrid;
    private TranslateTransform? _previewTranslateTransform;

    private bool _isDraggingSplitter;
    private double _splitterStartX;
    private double _editorStartWidth;

    private bool _isDraggingPreview;
    private Point _previewDragStart;
    private const double MinEditorWidth = 320;
    private const double MaxEditorWidth = 860;
    private const double MinPreviewWidth = 360;

    private const double ZoomStep = 0.1;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AttachPreviewHandlers();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void AttachPreviewHandlers()
    {
        _codeEditor = this.FindControl<TextEditor>("CodeEditor");
        _previewImage = this.FindControl<Image>("PreviewImageControl");
        _workspaceGrid = this.FindControl<Grid>("WorkspaceGrid");
        _splitterBorder = this.FindControl<Border>("SplitterBorder");
        _previewGrid = this.FindControl<Grid>("PreviewGrid");
        _previewImageBorder = this.FindControl<Border>("PreviewImageBorder");
        _previewTransformControl = this.FindControl<LayoutTransformControl>("PreviewTransformControl");
        _previewContentGrid = this.FindControl<Grid>("PreviewContentGrid");

        if (_previewTransformControl?.LayoutTransform is TransformGroup transformGroup)
        {
            foreach (var transform in transformGroup.Children)
            {
                if (transform is TranslateTransform translateTransform)
                {
                    _previewTranslateTransform = translateTransform;
                    break;
                }
            }
        }

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

        if (_previewImage != null)
        {
            _previewImage.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == nameof(Image.Source))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        ResetPreviewOffset();
                        UpdatePreviewFitScale();
                    }, DispatcherPriority.Background);
                }
            };
        }
    }

    private void ResetPreviewOffset()
    {
        if (_previewTranslateTransform != null)
        {
            _previewTranslateTransform.X = 0;
            _previewTranslateTransform.Y = 0;
        }
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
            }, DispatcherPriority.Background);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.SaveSettings();
        }
    }

    private void OnSplitterPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDraggingSplitter = true;
            _splitterStartX = e.GetPosition(this).X;
            _editorStartWidth = _viewModel?.EditorPanelWidth ?? 640;
            e.Pointer.Capture(_splitterBorder);
            e.Handled = true;
        }
    }

    private void OnSplitterPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingSplitter || _viewModel == null || _workspaceGrid == null)
            return;

        var currentX = e.GetPosition(this).X;
        var deltaX = currentX - _splitterStartX;
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
            _isDraggingSplitter = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(_previewGrid).Properties.IsLeftButtonPressed)
        {
            _isDraggingPreview = true;
            _previewDragStart = e.GetPosition(_previewGrid);
            e.Pointer.Capture(_previewGrid);
            _previewGrid!.Cursor = new Cursor(StandardCursorType.Hand);
            e.Handled = true;
        }
    }

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDraggingPreview || _previewTranslateTransform == null)
            return;

        var currentPos = e.GetPosition(_previewGrid);
        var delta = currentPos - _previewDragStart;
        _previewDragStart = currentPos;

        _previewTranslateTransform.X += delta.X;
        _previewTranslateTransform.Y += delta.Y;
        e.Handled = true;
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isDraggingPreview)
        {
            _isDraggingPreview = false;
            e.Pointer.Capture(null);
            if (_previewGrid != null)
            {
                _previewGrid.Cursor = Cursor.Default;
            }
            e.Handled = true;
        }
    }

    private void OnPreviewPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || _previewTranslateTransform == null || _previewGrid == null)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var mousePos = e.GetPosition(_previewGrid);
            var viewportCenter = new Point(_previewGrid.Bounds.Width / 2, _previewGrid.Bounds.Height / 2);
            var zoomCenter = mousePos;

            var oldZoom = viewModel.PreviewZoom;
            double newZoom;

            if (e.Delta.Y > 0)
            {
                newZoom = Math.Min(oldZoom + ZoomStep, MaxZoom);
            }
            else
            {
                newZoom = Math.Max(oldZoom - ZoomStep, MinZoom);
            }

            if (Math.Abs(newZoom - oldZoom) < 0.001) return;

            var oldScale = viewModel.PreviewDisplayScale;
            viewModel.PreviewZoom = newZoom;
            var newScale = viewModel.PreviewDisplayScale;
            var scaleRatio = newScale / oldScale;

            var imageCenterX = viewportCenter.X + _previewTranslateTransform.X;
            var imageCenterY = viewportCenter.Y + _previewTranslateTransform.Y;

            var dx = zoomCenter.X - imageCenterX;
            var dy = zoomCenter.Y - imageCenterY;

            _previewTranslateTransform.X -= dx * (scaleRatio - 1);
            _previewTranslateTransform.Y -= dy * (scaleRatio - 1);

            e.Handled = true;
        }
    }

    private void OnPreviewViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdatePreviewFitScale();
    }

    private void OnWorkspaceSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateWorkspaceLayout();
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
}
