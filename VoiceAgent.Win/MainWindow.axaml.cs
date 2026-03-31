using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Platform.Storage; // 🌟 就是少這行，讓它認識 TryGetLocalPath！
using SharpHook;
using SharpHook.Native;
using System;
using System.IO;
using System.Linq;
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

            // 🌟 【新增】智慧型自動註冊：啟動即自動加入環境變數
            AutoRegisterEnvironmentPath();

            // 🌟 註冊 Avalonia 的拖曳放入事件
            AddHandler(DragDrop.DropEvent, OnFileDropped);

            CheckAndLoadKey();

            _hook = new TaskPoolGlobalHook();
            _hook.KeyPressed += OnKeyPressed;
            _hook.RunAsync();
        }
        private void AutoRegisterEnvironmentPath()
        {
            try
            {
                // 1. 取得當前 exe 所在的資料夾路徑
                string? exePath = Environment.ProcessPath;
                string? dirPath = Path.GetDirectoryName(exePath);

                if (string.IsNullOrEmpty(dirPath)) return;

                // 2. 取得目前使用者的 Path 變數
                var scope = EnvironmentVariableTarget.User;
                string oldPath = Environment.GetEnvironmentVariable("Path", scope) ?? "";

                // 3. 檢查是否已經存在 (不分大小寫)
                if (!oldPath.Split(';').Any(p => p.Trim().Equals(dirPath, StringComparison.OrdinalIgnoreCase)))
                {
                    // 4. 如果不在，就幫使用者接上去
                    string newPath = oldPath.EndsWith(";") ? oldPath + dirPath : oldPath + ";" + dirPath;
                    Environment.SetEnvironmentVariable("Path", newPath, scope);

                    // 這裡可以印個 Log，或者在第一次啟動時彈個通知
                    Console.WriteLine($"[System] 自動註冊環境變數成功: {dirPath}");
                }
            }
            catch (Exception ex)
            {
                // 如果權限不足或其他原因失敗，靜默處理，不影響主程式啟動
                Log("Error", $"環境變數註冊失敗: {ex.Message}");
            }
        }
        // ==========================================
        // 【全新邏輯】檢查金鑰，決定要隱藏還是顯示拖曳提示
        // ==========================================
        private void CheckAndLoadKey()
        {
            if (File.Exists(Program.ConfigPath))
            {
                string path = File.ReadAllText(Program.ConfigPath).Trim();
                if (File.Exists(path) && path.EndsWith(".json"))
                {
                    _isKeyValid = true;
                    _speechService = new CrossPlatformSpeechService(path);
                    _errorCorrectionService = new ErrorCorrectionService();
                    _speechService.OnFinalResultReceived += OnSpeechResultReceived;

                    this.Hide(); // 有金鑰，乖乖躲到背景
                    Log("System", "✅ 引擎就緒");
                    return;
                }
            }

            // ⚠️ 沒有金鑰：強制顯示在螢幕下方，並提示使用者拖曳
            PositionWindow();
            this.Show();
            StatusText.Foreground = Avalonia.Media.Brushes.Yellow;
            StatusText.Text = "👉 請將 Google 金鑰 (.json) 拖曳到此視窗";
        }

        // ==========================================
        // 【終極 UX】處理使用者把檔案拖曳進視窗的動作
        // ==========================================
        private async void OnFileDropped(object? sender, DragEventArgs e)
        {
            // 抓取拖曳進來的檔案
            var files = e.Data.GetFiles();
            if (files != null)
            {
                var file = files.FirstOrDefault();
                if (file != null)
                {
                    string path = file.TryGetLocalPath() ?? "";

                    if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        // 儲存金鑰並重新載入
                        File.WriteAllText(Program.ConfigPath, path);
                        CheckAndLoadKey(); // 重新觸發載入邏輯

                        StatusText.Foreground = Avalonia.Media.Brushes.LimeGreen;
                        StatusText.Text = "✅ 設定完成！請雙擊 Ctrl 開始使用";
                        await Task.Delay(3000); // 顯示三秒後自動收合
                        this.Hide();
                    }
                    else
                    {
                        StatusText.Foreground = Avalonia.Media.Brushes.OrangeRed;
                        StatusText.Text = "❌ 格式錯誤，請拖曳 .json 檔案";
                        await Task.Delay(2000);
                        StatusText.Foreground = Avalonia.Media.Brushes.Yellow;
                        StatusText.Text = "👉 請將 Google 金鑰 (.json) 拖曳到此視窗";
                    }
                }
            }
        }

        private void PositionWindow()
        {
            if (this.Screens.Primary != null)
            {
                var rect = this.Screens.Primary.WorkingArea;
                this.Position = new Avalonia.PixelPoint((rect.Width - (int)this.Width) / 2, rect.Height - (int)this.Height - 100);
            }
        }

        private void Log(string tag, string msg) { Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {msg}"); Dispatcher.UIThread.Post(() => StatusText.Text = msg); }
        public void Window_PointerPressed(object? sender, PointerPressedEventArgs e) { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); }

        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
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
                PositionWindow();
                this.Show();

                if (!_isKeyValid)
                {
                    StatusText.Foreground = Avalonia.Media.Brushes.Yellow;
                    StatusText.Text = "👉 請將 Google 金鑰 (.json) 拖曳到此視窗";
                    return;
                }

                StatusText.Foreground = Avalonia.Media.Brushes.White;
                Log("Status", "🎙️ 聆聽中...");
                await _speechService!.StartListeningAsync();
                Dispatcher.UIThread.Post(() => this.Hide());
            }
        }

        private async void OnSpeechResultReceived(string transcript)
        {
            _speechService!.IsProcessing = true;
            Log("STT", $"原始辨識：「{transcript}」");

            _currentContextText = await WindowsInputInjector.ExtractCurrentTextAsync();
            var apiResponse = await _errorCorrectionService!.CorrectTextAsync(_currentContextText, transcript);

            string finalOut = apiResponse?.CorrectedText ?? transcript;
            bool isCommand = (apiResponse?.Type == "command");

            // 🌟 【強化比對邏輯】
            var keywords = Program.LoadKeywords();

            // 1. 建立一個「除噪」後的版本用來比對 (去掉空格與中文句號)
            string cleanOut = finalOut.Replace(" ", "").Replace("　", "").Replace("。", "");

            foreach (var kvp in keywords)
            {
                string key = kvp.Key.Trim();
                string value = kvp.Value.Trim();

                // 2. 如果除噪後的文字包含關鍵字，就在原始文字中執行替換
                if (cleanOut.Contains(key))
                {
                    finalOut = finalOut.Replace(key, value);
                }
            }

            Log("API", $"輸出：\"{finalOut}\"");

            if (isCommand) await WindowsInputInjector.ReplaceEntireTextAsync(finalOut);
            else await WindowsInputInjector.AppendTextAtEndAsync(finalOut);

            _speechService.ResetVoiceTimeout();
            _speechService.IsProcessing = false;
            Log("Status", "🎙️ 繼續聆聽...");
        }

        protected override void OnClosed(EventArgs e) { _hook?.Dispose(); base.OnClosed(e); }
    }
}