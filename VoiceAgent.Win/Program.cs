using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace VoiceAgent.Win
{
    internal class Program
    {
        // 🌟 【關鍵修復】取得 EXE 真正的所在資料夾
        public static readonly string AppDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppDomain.CurrentDomain.BaseDirectory;
        
        // 🌟 使用絕對路徑，確保無論在哪裡下指令，檔案都存在 EXE 旁邊
        public static readonly string ConfigPath = Path.Combine(AppDir, "voice_agent_config.txt");
        public static readonly string KeywordFile = Path.Combine(AppDir, "keywords.json");
        private const string AppName = "VoiceAssistant"; // 註冊表使用的名稱

        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        // 🌟 加上平台標示，解決 CA1416 註冊表警告
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        [STAThread]
        public static void Main(string[] args)
        {
            // 🌟 裝上終極黑盒子：捕捉所有靜默崩潰
            try
            {

                if (args.Length > 0)
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                    // 強制設定終端機編碼
                    Console.OutputEncoding = System.Text.Encoding.UTF8;
                    Console.InputEncoding = System.Text.Encoding.UTF8;
                    Console.WriteLine();

                    string command = args[0].ToLower();
                    string exePath = Environment.ProcessPath ?? "";

                    // 1. 說明文件
                    if (command == "-help" || command == "--help")
                    {
                        Console.WriteLine("==================================================");
                        Console.WriteLine($"{AppName} 指令手冊");
                        Console.WriteLine("==================================================");
                        Console.WriteLine($"{AppName} -key <路徑>              : 綁定 Google STT 金鑰 JSON");
                        Console.WriteLine($"{AppName} -addword <詞> <替換內容> : 新增/修改自訂替換詞彙");
                        Console.WriteLine($"{AppName} -delword <詞>            : 刪除特定的自訂詞彙");
                        Console.WriteLine($"{AppName} -listwords               : 列出目前所有的自訂詞彙");
                        Console.WriteLine($"{AppName} -autostart <true/false>  : 設定開機是否自動啟動助理");
                        Console.WriteLine($"{AppName} -reset                   : 清空所有設定 (金鑰與詞庫)");
                        Console.WriteLine($"{AppName} -setpath                 : (手動) 將程式加入全域環境變數");
                        Console.WriteLine("==================================================");
                        return;
                    }
                    // 2. 開機自啟動
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
                    // 3. 一鍵重設
                    else if (command == "-reset")
                    {
                        if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
                        if (File.Exists(KeywordFile)) File.Delete(KeywordFile);
                        Console.WriteLine("[Warning] 所有設定與詞庫已清空。");
                        return;
                    }
                    // 4. 設定環境變數
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
                    // 5. 其他指令 (拔除所有 Emoji 以防亂碼)
                    else if (command == "-key" || command == "--key")
                    {
                        if (args.Length > 1)
                        {
                            string keyPath = args[1].Replace("\"", "").Replace("'", "").Trim();
                            if (File.Exists(keyPath) && keyPath.EndsWith(".json"))
                            {
                                File.WriteAllText(ConfigPath, keyPath);
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
                // ⚠️ 如果發生任何閃退，立刻將錯誤訊息寫入 CRASH_LOG.txt
                File.WriteAllText(Path.Combine(AppDir, "CRASH_LOG.txt"), $"[{DateTime.Now}] 發生崩潰：\n{ex}");
            }
        }

        // 供主程式讀取詞庫的方法
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

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}