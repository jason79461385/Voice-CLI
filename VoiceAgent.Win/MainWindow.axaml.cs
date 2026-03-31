using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Native;
using System;
using System.IO;
using System.Threading.Tasks;

namespace VoiceAgent.Win
{
    public partial class MainWindow : Window
    {
        private TaskPoolGlobalHook? _hook; private DateTime _lastCtrlPressTime = DateTime.MinValue; private readonly object _keyLock = new object();
        private CrossPlatformSpeechService? _speechService; private ErrorCorrectionService? _errorCorrectionService;
        private string _currentContextText = ""; private bool _isKeyValid = false;

        public MainWindow()
        {
            InitializeComponent();
            string keyPath = "voice_agent_config.txt";
            if (File.Exists(keyPath))
            {
                string path = File.ReadAllText(keyPath).Trim();
                if (File.Exists(path))
                {
                    _isKeyValid = true;
                    _speechService = new CrossPlatformSpeechService(path);
                    _errorCorrectionService = new ErrorCorrectionService();
                    _speechService.OnFinalResultReceived += OnSpeechResultReceived;
                    Log("System", "✅ 引擎就緒 (支援自訂詞庫)");
                }
            }

            _hook = new TaskPoolGlobalHook(); _hook.KeyPressed += OnKeyPressed; _hook.RunAsync();
        }

        private void Log(string tag, string msg) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {msg}"); Dispatcher.UIThread.Post(() => StatusText.Text = msg); }
        public void Window_PointerPressed(object? sender, PointerPressedEventArgs e) { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); }

        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            // 【核心修復】：如果神之手正在貼上文字，忽略所有鍵盤監聽！
            // 防止神之手送出的 Ctrl 鍵被誤判為使用者雙擊。
            if (_speechService != null && _speechService.IsProcessing) return;

            string key = e.Data.KeyCode.ToString();
            if (key.Contains("Control") || key.Contains("Ctrl"))
            {
                lock (_keyLock)
                {
                    if ((DateTime.Now - _lastCtrlPressTime).TotalMilliseconds < 500) { Dispatcher.UIThread.Post(() => ToggleUI()); _lastCtrlPressTime = DateTime.MinValue; }
                    else { _lastCtrlPressTime = DateTime.Now; }
                }
            }
        }

        private async void ToggleUI()
        {
            if (this.IsVisible) { this.Hide(); if (_isKeyValid && _speechService != null && _speechService.IsListening) _speechService.StopListening(); }
            else
            {
                if (this.Screens.Primary != null) { var rect = this.Screens.Primary.WorkingArea; this.Position = new Avalonia.PixelPoint((rect.Width - (int)this.Width) / 2, rect.Height - (int)this.Height - 100); }
                this.Show();
                if (!_isKeyValid) 
                {
                    StatusText.Foreground = Avalonia.Media.Brushes.OrangeRed;
                    Log("Warning", "⚠️ 請關閉程式，並使用 -key 指令綁定金鑰！");
                    await Task.Delay(3500); this.Hide(); return; 
                }
                StatusText.Foreground = Avalonia.Media.Brushes.White;
                Log("Status", "🎙️ 聆聽中...");
                await _speechService!.StartListeningAsync();
                Dispatcher.UIThread.Post(() => this.Hide());
            }
        }

        private async void OnSpeechResultReceived(string transcript)
        {
            _speechService!.IsProcessing = true; // 開啟神之手護盾，防止觸發 Ctrl
            Log("STT", $"聽到：「{transcript}」");

            _currentContextText = await WindowsInputInjector.ExtractCurrentTextAsync();
            var apiResponse = await _errorCorrectionService!.CorrectTextAsync(_currentContextText, transcript);

            // ==========================================
            // 處理輸出與「自訂關鍵字替換」
            // ==========================================
            string finalOut = transcript;
            bool isCommand = false;

            if (apiResponse != null)
            {
                finalOut = apiResponse.CorrectedText;
                isCommand = (apiResponse.Type == "command");
            }

            // 在貼上之前，讀取字典並進行替換
            var keywords = Program.LoadKeywords();
            foreach (var kvp in keywords)
            {
                finalOut = finalOut.Replace(kvp.Key, kvp.Value);
            }

            Log("API", $"{(isCommand ? "執行覆寫" : "執行附加")}: \"{finalOut}\"");

            if (isCommand) await WindowsInputInjector.ReplaceEntireTextAsync(finalOut);
            else await WindowsInputInjector.AppendTextAtEndAsync(finalOut);

            _speechService.ResetVoiceTimeout();
            _speechService.IsProcessing = false; // 關閉神之手護盾
            Log("Status", "🎙️ 繼續聆聽...");
        }

        protected override void OnClosed(EventArgs e) { _hook?.Dispose(); base.OnClosed(e); }
    }
}