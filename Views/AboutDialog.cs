using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Mermaider.Views;

public sealed class AboutDialog : Window
{
    public AboutDialog(string appName, string features, string author, string version)
    {
        Title = "关于";
        Width = 460;
        Height = 250;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = appName,
                    FontSize = 24,
                    FontWeight = FontWeight.SemiBold
                },
                CreateInfoLine("功能", features),
                CreateInfoLine("作者", author),
                CreateInfoLine("版本", version),
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 8, 0, 0),
                    Children =
                    {
                        CreateButton("确定", (_, _) => Close())
                    }
                }
            }
        };
    }

    private static Control CreateInfoLine(string label, string value)
    {
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"{label}:",
                    Width = 48,
                    FontWeight = FontWeight.Medium
                },
                new TextBlock
                {
                    Text = value,
                    TextWrapping = TextWrapping.Wrap,
                    Width = 340
                }
            }
        };
    }

    private static Button CreateButton(string text, EventHandler<RoutedEventArgs> onClick)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 88,
            IsDefault = true
        };
        button.Click += onClick;
        return button;
    }
}
