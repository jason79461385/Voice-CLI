using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using SharpHook;
using SharpHook.Native;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VoiceAgent.Win
{
    public partial class MainWindow : Window
    {
        private TaskPoolGlobalHook? _hook;
        private DateTime _lastCtrlPressTime = DateTime.MinValue;
        private DateTime _lastToggleTime    = DateTime.MinValue;
        private readonly object _keyLock    = new object();

        // 🌟 新增：用來記錄兩次 Ctrl 之間，是否有按過其他按鍵 (解決 Ctrl+C/V 誤觸)
        private bool _hasOtherKeyInterfered = false;

        private CrossPlatformSpeechService? _speechService;
        private ErrorCorrectionService?     _errorCorrectionService;
        private bool _isKeyValid = false;

        private Dictionary<string, string> _keywordsCache = new Dictionary<string, string>();
        private FileSystemWatcher? _keywordWatcher;

        private IntPtr _targetWindowHandle = IntPtr.Zero;
        private readonly string _logFilePath;

        public MainWindow()
        {
            InitializeComponent();
            _logFilePath = Path.Combine(Program.AppDir, "debug_log.txt");
            WriteDebugLog("=== 應用程式啟動 ===");

            AutoRegisterEnvironmentPath();
            AddHandler(DragDrop.DropEvent, OnFileDropped);
            
            _keywordsCache = Program.LoadKeywords();
            SetupKeywordWatcher();
            CheckAndLoadKey();

            _hook = new TaskPoolGlobalHook();
            _hook.KeyPressed += OnKeyPressed;
            _hook.RunAsync();
        }

        private void WriteDebugLog(string message)
        {
            try
            {
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                File.AppendAllText(_logFilePath, $"[{timestamp}] {message}{Environment.NewLine}");
                Console.WriteLine($"[{timestamp}] {message}");
            }
            catch { }
        }

        private void SetupKeywordWatcher()
        {
            try
            {
                _keywordWatcher = new FileSystemWatcher(Program.AppDir, "keywords.json");
                _keywordWatcher.NotifyFilter = NotifyFilters.LastWrite;
                _keywordWatcher.Changed += (s, e) => {
                    Task.Delay(300).ContinueWith(_ => {
                        _keywordsCache = Program.LoadKeywords();
                        Dispatcher.UIThread.Post(() => Log("System", "🔄 詞庫快取已更新"));
                    });
                };
                _keywordWatcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        private void CheckAndLoadKey()
        {
            var config = Program.LoadConfig();

            if (!string.IsNullOrEmpty(config.GoogleKeyPath) && File.Exists(config.GoogleKeyPath))
            {
                _isKeyValid = true;
                _speechService = new CrossPlatformSpeechService(config.GoogleKeyPath);
                
                _speechService.NoiseThreshold = config.Threshold;
                _speechService.HoldTimeSeconds = config.HoldTime;

                _errorCorrectionService = new ErrorCorrectionService();

                _speechService.OnPartialResultReceived += OnPartialResult;
                _speechService.OnFinalResultReceived   += OnFinalResult;
                _speechService.OnListeningStopped += () => Dispatcher.UIThread.Post(() => this.Hide());

                this.Hide();
                Log("System", "✅ 引擎就緒");
                _ = PreWarmApiAsync();
                return;
            }

            PositionWindow();
            this.Show();
            StatusText.Foreground = Avalonia.Media.Brushes.Yellow;
            StatusText.Text = "👉 請點擊此視窗來選擇 Google 金鑰 (.json)";
        }

        private async Task PreWarmApiAsync() { try { await _errorCorrectionService!.CorrectTextAsync("", "prewarm"); } catch { } }

        private void AutoRegisterEnvironmentPath()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                string? dirPath = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(dirPath)) return;

                var scope = EnvironmentVariableTarget.User;
                string oldPath = Environment.GetEnvironmentVariable("Path", scope) ?? "";

                if (!oldPath.Split(';').Any(p => p.Trim().Equals(dirPath, StringComparison.OrdinalIgnoreCase)))
                {
                    Environment.SetEnvironmentVariable("Path", oldPath.EndsWith(";") ? oldPath + dirPath : oldPath + ";" + dirPath, scope);
                }
            }
            catch { }
        }

        // 🌟 核心修復：嚴謹的 Ctrl 雙擊判斷
        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            if (_speechService != null && _speechService.IsProcessing) return;

            string key = e.Data.KeyCode.ToString();

            // 判斷按下的如果是 Ctrl
            if (key.Contains("Control") || key.Contains("Ctrl"))
            {
                lock (_keyLock)
                {
                    DateTime now = DateTime.Now;

                    // 1. 防抖：如果兩次 Ctrl 間隔小於 50 毫秒，通常是長按產生的連發訊號，直接忽略
                    double diff = (now - _lastCtrlPressTime).TotalMilliseconds;
                    if (diff < 50) return;

                    // 防止連續開關過快
                    if ((now - _lastToggleTime).TotalMilliseconds < 800) return;

                    // 2. 純淨雙擊判定：必須在 500 毫秒內，且期間「沒有」按過其他鍵
                    if (!_hasOtherKeyInterfered && diff < 500)
                    {
                        _lastToggleTime = now;
                        _lastCtrlPressTime = DateTime.MinValue; // 重置

                        if (_speechService != null && _speechService.IsListening)
                        {
                            WriteDebugLog("[Event] 雙擊 Ctrl：主動取消聆聽");
                            _speechService.StopListening();
                            Dispatcher.UIThread.Post(() => this.Hide());
                        }
                        else
                        {
                            _targetWindowHandle = NativeInputInjector.GetForegroundWindow();
                            WriteDebugLog($"[Event] 雙擊 Ctrl：啟動聆聽。擷取當前目標 Handle: {_targetWindowHandle}");
                            Dispatcher.UIThread.Post(() => StartListening());
                        }
                    }
                    else
                    {
                        // 紀錄第一次按下 Ctrl 的時間，並重置干擾標記準備迎接第二次
                        _lastCtrlPressTime = now;
                        _hasOtherKeyInterfered = false;
                    }
                }
            }
            else
            {
                // 🌟 關鍵：如果按下的不是 Ctrl (例如按了 C, V, A...)，標記為已干擾
                _hasOtherKeyInterfered = true;
            }
        }

        private void StartListening()
        {
            if (!_isKeyValid || _speechService == null || _speechService.IsListening) return;

            PositionWindow();
            this.Show();
            StatusText.Foreground = Avalonia.Media.Brushes.White;
            StatusText.Text = "🎙️ 聆聽中...";

            _ = _speechService.StartListeningAsync();
        }

        private void OnPartialResult(string partial) 
        { 
            if (string.IsNullOrWhiteSpace(partial)) return; 
            Dispatcher.UIThread.Post(() => StatusText.Text = $"🎙️ {partial}"); 
        }

        private async void OnFinalResult(string transcript)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(transcript))
                {
                    WriteDebugLog("[STT] 收到空白 transcript，跳過注入");
                    return;
                }

                _speechService!.IsProcessing = true;
                Log("STT", $"聽到：「{transcript}」");

                string context = "";
                try { context = await WindowsInputInjector.ExtractCurrentTextAsync(); } catch { }
                
                var apiResponse = await _errorCorrectionService!.CorrectTextAsync(context, transcript);

                string finalOut = apiResponse?.CorrectedText ?? transcript;
                bool isCommand = (apiResponse?.Type == "command");

                string cleanOut = finalOut.Replace(" ", "").Replace("　", "").Replace("。", "");
                foreach (var kvp in _keywordsCache)
                {
                    if (cleanOut.Contains(kvp.Key.Trim())) finalOut = finalOut.Replace(kvp.Key.Trim(), kvp.Value.Trim());
                }

                Log("API", $"修正輸出：\"{finalOut}\"");
                WriteDebugLog($"[Output] 準備輸出文字。目標 Handle: {_targetWindowHandle}");

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    string? originalClipboard = null;
                    try
                    {
                        originalClipboard = await TextCopy.ClipboardService.GetTextAsync();
                        await TextCopy.ClipboardService.SetTextAsync(finalOut);
                        WriteDebugLog("[Output] 語音文字已寫入剪貼簿");
                    }
                    catch (Exception ex) { WriteDebugLog($"[Error] 剪貼簿操作失敗: {ex.Message}"); }

                    try { NativeInputInjector.ForceForegroundWindow(_targetWindowHandle); } catch { }
                    WriteDebugLog("[Output] 焦點切換完成。");

                    await Task.Delay(200);

                    WriteDebugLog("[Output] 開始執行 Native 注入...");
                    try 
                    {
                        if (isCommand) await NativeInputInjector.SelectAllThenPasteAsync();
                        else await NativeInputInjector.PasteFromClipboardAsync();
                        WriteDebugLog("[Output] 注入完成！"); 
                    } 
                    catch (Exception ex) { WriteDebugLog($"[Error] 注入失敗: {ex.Message}"); }

                    await Task.Delay(200); 
                    if (originalClipboard != null)
                    {
                        try 
                        {
                            await TextCopy.ClipboardService.SetTextAsync(originalClipboard);
                            WriteDebugLog("[Output] 使用者原始剪貼簿已還原");
                        }
                        catch (Exception ex) { WriteDebugLog($"[Error] 剪貼簿還原失敗: {ex.Message}"); }
                    }
                });
            }
            catch (Exception ex)
            {
                WriteDebugLog($"[Error] OnFinalResult 發生未預期錯誤: {ex.Message}");
            }
            finally
            {
                if (_speechService != null)
                {
                    _speechService.ResetVoiceTimeout();
                    _speechService.IsProcessing = false;
                }
                Log("Status", "✅ 已完成！繼續聆聽...");
            }
        }

        private async void OnFileDropped(object? sender, DragEventArgs e)
        {
#pragma warning disable CS0618
            var files = e.Data.GetFiles();
#pragma warning restore CS0618
            if (files != null && files.FirstOrDefault() is var file && file != null)
            {
                string path = file.TryGetLocalPath() ?? "";
                if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var config = Program.LoadConfig();
                    config.GoogleKeyPath = path;
                    Program.SaveConfig(config);

                    CheckAndLoadKey();
                    StatusText.Foreground = Avalonia.Media.Brushes.LimeGreen;
                    StatusText.Text = "✅ 設定完成！請雙擊 Ctrl 開始使用";
                    await Task.Delay(3000);
                    this.Hide();
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

        private void Log(string tag, string msg)
        {
            WriteDebugLog($"[{tag}] {msg}");
            Dispatcher.UIThread.Post(() => StatusText.Text = msg);
        }

        public async void Window_PointerPressed(object? sender, PointerPressedEventArgs e) 
        { 
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) 
            {
                if (!_isKeyValid)
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel != null)
                    {
                        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                        {
                            Title = "選擇 Google 金鑰 JSON 檔案",
                            AllowMultiple = false,
                            FileTypeFilter = new[] { new FilePickerFileType("JSON Files") { Patterns = new[] { "*.json" } } }
                        });

                        if (files.Count >= 1)
                        {
                            string path = files[0].TryGetLocalPath() ?? "";
                            if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                            {
                                var config = Program.LoadConfig();
                                config.GoogleKeyPath = path;
                                Program.SaveConfig(config);

                                CheckAndLoadKey();
                                StatusText.Foreground = Avalonia.Media.Brushes.LimeGreen;
                                StatusText.Text = "✅ 設定完成！請雙擊 Ctrl 開始使用";
                                await Task.Delay(3000);
                                this.Hide();
                            }
                            else
                            {
                                StatusText.Foreground = Avalonia.Media.Brushes.OrangeRed;
                                StatusText.Text = "❌ 格式錯誤，請選擇 .json 檔案";
                                await Task.Delay(2000);
                                StatusText.Foreground = Avalonia.Media.Brushes.Yellow;
                                StatusText.Text = "👉 請點擊此視窗來選擇 Google 金鑰 (.json)";
                            }
                        }
                    }
                }
                else
                {
                    BeginMoveDrag(e); 
                }
            }
        }

        protected override void OnClosed(EventArgs e) { WriteDebugLog("=== 應用程式關閉 ==="); _hook?.Dispose(); base.OnClosed(e); }
    }
}