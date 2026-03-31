using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace VoiceAgent.Win // 🌟 確保這裡跟你的 App.axaml.cs 一樣
{
    public partial class WelcomeWindow : Window
    {
        public WelcomeWindow()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        // 🌟 處理按鈕點擊關閉視窗
        public void CloseClick(object? sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}