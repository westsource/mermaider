using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaWebView;
using Mermaider.Services;
using Mermaider.ViewModels;
using Mermaider.Views;

namespace Mermaider;

public class App : Application
{
    public override void RegisterServices()
    {
        base.RegisterServices();
        AvaloniaWebViewBuilder.Initialize(default);
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mermaidService = new MermaidService();
            var settingsService = new SettingsService();
            var fileService = new FileService();

            var mainWindow = new MainWindow();
            var viewModel = new MainViewModel(
                mermaidService,
                fileService,
                settingsService,
                mainWindow.StorageProvider,
                mainWindow
            );

            mainWindow.DataContext = viewModel;

            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && File.Exists(args[1]))
            {
                viewModel.OpenFileFromPath(args[1]).Wait();
            }

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
