using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Data.Converters;
using Mermaider.Models;
using Mermaider.ViewModels;

namespace Mermaider.Views;

public partial class AIPanel : UserControl
{
    public AIPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetupInputTextBoxContextMenu();
    }

    private void SetupInputTextBoxContextMenu()
    {
        var inputTextBox = this.FindControl<TextBox>("InputTextBox");
        if (inputTextBox == null) return;

        var menu = new ContextMenu();
        
        var cutItem = new MenuItem { Header = "剪切(_X)", HotKey = KeyGesture.Parse("Ctrl+X") };
        cutItem.Click += (_, _) => inputTextBox.Cut();
        
        var copyItem = new MenuItem { Header = "复制(_C)", HotKey = KeyGesture.Parse("Ctrl+C") };
        copyItem.Click += (_, _) => inputTextBox.Copy();
        
        var pasteItem = new MenuItem { Header = "粘贴(_V)", HotKey = KeyGesture.Parse("Ctrl+V") };
        pasteItem.Click += (_, _) => inputTextBox.Paste();
        
        var selectAllItem = new MenuItem { Header = "全选(_A)", HotKey = KeyGesture.Parse("Ctrl+A") };
        selectAllItem.Click += (_, _) => inputTextBox.SelectAll();

        var items = new List<Control>
        {
            cutItem,
            copyItem,
            pasteItem,
            new Separator(),
            selectAllItem
        };
        
        menu.ItemsSource = items;
        inputTextBox.ContextMenu = menu;
    }

    private void OnTogglePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is AIPanelViewModel vm)
        {
            vm.ToggleCommand.Execute(null);
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            e.Handled = true;
            if (DataContext is AIPanelViewModel vm && vm.SendCommand.CanExecute(null))
            {
                vm.SendCommand.Execute(null);
            }
        }
    }
}

public static class AIPanelConverters
{
    public static readonly IValueConverter MessageBackgroundConverter = new FuncValueConverter<MessageRole, IBrush>(
        role => role == MessageRole.User 
            ? new SolidColorBrush(Color.Parse("#E3F2FD")) 
            : new SolidColorBrush(Color.Parse("#FFFFFF"))
    );

    public static readonly IValueConverter MessageAlignmentConverter = new FuncValueConverter<MessageRole, Avalonia.Layout.HorizontalAlignment>(
        role => role == MessageRole.User 
            ? Avalonia.Layout.HorizontalAlignment.Right 
            : Avalonia.Layout.HorizontalAlignment.Left
    );
}
