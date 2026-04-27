using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Principal;
using System.Linq;
using Pv; // 🌟 引入 PvRecorder 進行環境測噪

namespace VoiceAgent.Win
{
    // 🌟 統一的 JSON 配置檔模型
    public class AppConfig
    {
        public string GoogleKeyPath { get; set; } = "";
        public int Threshold { get; set; } = 200;
        public double HoldTime { get; set; } = 3.0; // 預設 0.4 秒極速模式
    }

    internal class Program
    {
        public static readonly string AppDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string ConfigPath = Path.Combine(AppDir, "voice_agent_config.json"); // 改用 JSON 統一管理
        public static readonly string KeywordFile = Path.Combine(AppDir, "keywords.json");
        private const string AppName = "VoiceAssistant";

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        [STAThread]
        public static void Main(string[] args)
        {
            // 🌟 強制管理員檢查與自動提權 (解決 UAC 權限與焦點問題)
            if (!IsAdministrator())
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    Verb = "runas"
                };
                try { Process.Start(psi); } catch { }
                return;
            }

            try
            {
                var config = LoadConfig();

                if (args.Length > 0)
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    Console.InputEncoding = System.Text.Encoding.UTF8;
                    Console.WriteLine();

                    string command = args[0].ToLower();
                    string exePath = Environment.ProcessPath ?? "";

                    // ==================================================
                    // 🌟 新增的進階設定指令 (-calibrate, -threshold, -holdtime)
                    // ==================================================
                    if (command == "-calibrate") 
                    {
                        Console.WriteLine("🎙️ 開始偵測環境噪音，請保持安靜 3 秒鐘...");
                        try
                        {
                            using var recorder = PvRecorder.Create(512, -1);
                            recorder.Start();
                            double totalVolume = 0;
                            int frames = 0;
                            DateTime start = DateTime.Now;

                            while ((DateTime.Now - start).TotalSeconds < 3)
                            {
                                short[] frame = recorder.Read();
                                long sum = 0;
                                foreach (var s in frame) sum += Math.Abs(s);
                                totalVolume += sum / (double)frame.Length;
                                frames++;
                            }
                            recorder.Stop();

                            double avgVolume = totalVolume / frames;
                            int suggestedThreshold = (int)Math.Ceiling(avgVolume) + 150; // 平均底噪 + 150 緩衝

                            Console.WriteLine($"[Info] 測量完畢！目前環境平均底噪為: {avgVolume:F0}");
                            Console.WriteLine($"[OK] 已自動為您設定最佳噪音閥值為: {suggestedThreshold}");
                            
                            config.Threshold = suggestedThreshold;
                            SaveConfig(config);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[Error] 麥克風偵測失敗: {ex.Message}");
                        }
                        return;
                    }
                    else if (command == "-threshold")
                    {
                        if (args.Length > 1 && int.TryParse(args[1], out int val))
                        {
                            config.Threshold = val;
                            SaveConfig(config);
                            Console.WriteLine($"[OK] 噪音閥值已手動設定為: {val}");
                        }
                        else Console.WriteLine("[Error] 請輸入數值。");
                        return;
                    }
                    else if (command == "-holdtime")
                    {
                        if (args.Length > 1 && double.TryParse(args[1], out double val))
                        {
                            if (val < 0.2 || val > 1.5)
                            {
                                Console.WriteLine("[Error] 錯誤：等待時間必須在 0.2 ~ 1.5 秒之間！");
                            }
                            else
                            {
                                config.HoldTime = val;
                                SaveConfig(config);
                                Console.WriteLine($"[OK] 停頓等待時間已設定為: {val} 秒");
                            }
                        }
                        else Console.WriteLine("[Error] 請輸入數值。");
                        return;
                    }

                    // ==================================================
                    // 🌟 以下是你嘔心瀝血寫的原始指令 (一字不漏為你保留！)
                    // ==================================================
                    else if (command == "-help" || command == "--help")
                    {
                        Console.WriteLine("==================================================");
                        Console.WriteLine($"{AppName} 指令手冊 (極速優化版)");
                        Console.WriteLine("==================================================");
                        Console.WriteLine($"-calibrate                 : 🌟 [推薦] 自動偵測環境底噪並設定最佳閥值");
                        Console.WriteLine($"-threshold <數值>          : 手動設定噪音閥值 (目前: {config.Threshold})");
                        Console.WriteLine($"-holdtime <秒數>           : 設定等待秒數 0.2~1.5 (目前: {config.HoldTime} 秒)");
                        Console.WriteLine($"{AppName} -key <路徑>              : 綁定 Google STT 金鑰 JSON");
                        Console.WriteLine($"{AppName} -addword <詞> <替換內容> : 新增/修改自訂替換詞彙");
                        Console.WriteLine($"{AppName} -delword <詞>            : 刪除特定的自訂詞彙");
                        Console.WriteLine($"{AppName} -listwords               : 列出目前所有的自訂詞彙");
                        Console.WriteLine($"{AppName} -autostart <true/false>  : 設定開機是否自動啟動助理");
                        Console.WriteLine($"{AppName} -reset                   : 清空所有設定 (金鑰與詞庫)");
                        Console.WriteLine($"{AppName} -setpath                 : (手動) 將程式加入全域環境變數");
                        Console.WriteLine("--------------------------------------------------");
                        Console.WriteLine("💡 【如何獲取 Google STT 金鑰 (JSON)？】");
                        Console.WriteLine("  1. 前往 Google Cloud Console (https://console.cloud.google.com/)");
                        Console.WriteLine("  2. 建立新專案，並搜尋啟用「Cloud Speech-to-Text API」");
                        Console.WriteLine("  3. 前往「API 與服務」>「憑證」，建立一個「服務帳戶」");
                        Console.WriteLine("  4. 點選該服務帳戶的「金鑰」分頁，選擇「新增金鑰」>「JSON」並下載");
                        Console.WriteLine("==================================================");
                        return;
                    }
                    else if (command == "-autostart")
                    {
                        bool enable = args.Length > 1 && args[1].ToLower() == "true";
                        using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (key != null)
                            {
                                if (enable)
                                {
                                    key.SetValue(AppName, $"\"{exePath}\"");
                                    Console.WriteLine("[OK] 已開啟開機自動啟動。");
                                }
                                else
                                {
                                    key.DeleteValue(AppName, false);
                                    Console.WriteLine("[OK] 已關閉開機自動啟動。");
                                }
                            }
                        }
                        return;
                    }
                    else if (command == "-reset")
                    {
                        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                        if (File.Exists(KeywordFile)) File.Delete(KeywordFile);
                        Console.WriteLine("[Warning] 所有設定與詞庫已清空。");
                        return;
                    }
                    else if (command == "-setpath")
                    {
                        string? dirPath = Path.GetDirectoryName(exePath);
                        if (dirPath != null)
                        {
                            var scope = EnvironmentVariableTarget.User;
                            string oldPath = Environment.GetEnvironmentVariable("Path", scope) ?? "";
                            if (!oldPath.Contains(dirPath))
                            {
                                Environment.SetEnvironmentVariable("Path", oldPath + ";" + dirPath, scope);
                                Console.WriteLine($"[OK] 已加入環境變數！請重啟終端機後輸入 '{AppName}' 測試。");
                            }
                            else Console.WriteLine("[Info] 此路徑已存在於環境變數中。");
                        }
                        return;
                    }
                    else if (command == "-key" || command == "--key")
                    {
                        if (args.Length > 1)
                        {
                            string keyPath = args[1].Replace("\"", "").Replace("'", "").Trim();
                            if (File.Exists(keyPath) && keyPath.EndsWith(".json"))
                            {
                                config.GoogleKeyPath = keyPath; // 寫入 JSON 設定檔
                                SaveConfig(config);
                                Console.WriteLine($"[OK] 金鑰已成功綁定: {keyPath}");
                            }
                            else Console.WriteLine($"[Error] 找不到金鑰檔案 '{keyPath}'。");
                        }
                        else Console.WriteLine("[Error] 請提供金鑰路徑。");
                        Environment.Exit(0);
                    }
                    else if (command == "-addword" || command == "--addword")
                    {
                        if (args.Length > 2)
                        {
                            var dict = LoadKeywords();
                            dict[args[1]] = args[2];
                            SaveKeywords(dict);
                            Console.WriteLine($"[OK] 已儲存專屬詞彙: '{args[1]}' -> '{args[2]}'");
                        }
                        else Console.WriteLine("[Error] 參數不足。範例: -addword 我的地址 台北市信義區101號");
                        Environment.Exit(0);
                    }
                    else if (command == "-delword" || command == "--delword")
                    {
                        if (args.Length > 1)
                        {
                            var dict = LoadKeywords();
                            if (dict.Remove(args[1]))
                            {
                                SaveKeywords(dict);
                                Console.WriteLine($"[Deleted] 已刪除專屬詞彙: '{args[1]}'");
                            }
                            else Console.WriteLine($"[Warning] 詞庫中找不到: '{args[1]}'");
                        }
                        Environment.Exit(0);
                    }
                    else if (command == "-listwords" || command == "--listwords")
                    {
                        var dict = LoadKeywords();
                        Console.WriteLine("--- 專屬詞庫列表 ---");
                        if (dict.Count == 0) Console.WriteLine("  (目前空空如也)");
                        foreach (var kvp in dict) Console.WriteLine($"  * {kvp.Key} -> {kvp.Value}");
                        Environment.Exit(0);
                    }

                    Environment.Exit(0); 
                }

                // ==========================================
                // 模式 B：GUI 懸浮視窗模式 (無參數)
                // ==========================================
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(AppDir, "CRASH_LOG.txt"), $"[{DateTime.Now}] 發生崩潰：\n{ex}");
            }
        }

        // ==========================================
        // 輔助方法區塊
        // ==========================================
        private static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static AppConfig LoadConfig()
        {
            if (!File.Exists(ConfigPath)) return new AppConfig();
            try { return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath)) ?? new AppConfig(); }
            catch { return new AppConfig(); }
        }

        public static void SaveConfig(AppConfig config)
        {
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static Dictionary<string, string> LoadKeywords()
        {
            if (!File.Exists(KeywordFile)) return new Dictionary<string, string>();
            try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(KeywordFile)) ?? new Dictionary<string, string>(); }
            catch { return new Dictionary<string, string>(); }
        }

        private static void SaveKeywords(Dictionary<string, string> dict)
        {
            File.WriteAllText(KeywordFile, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }

        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}