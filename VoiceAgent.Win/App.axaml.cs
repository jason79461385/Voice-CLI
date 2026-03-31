using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;

namespace VoiceAgent.Win
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 🌟 使用 Program.ConfigPath 絕對路徑
                if (!File.Exists(Program.ConfigPath)) 
                {
                    var welcome = new WelcomeWindow();
                    welcome.Show();
                }
                desktop.MainWindow = new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }

        // 🌟 選單點擊：退出
        private void OnMenuExitClick(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(); // 乾淨地關閉所有資源並退出
            }
        }

        // 🌟 選單點擊：顯示視窗 (如果使用者想看提示)
        private void OnMenuShowClick(object? sender, EventArgs e)
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Show();
            }
        }
    }
}