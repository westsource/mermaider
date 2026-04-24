using System;
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
