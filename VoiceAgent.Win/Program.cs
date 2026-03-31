using Avalonia;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace VoiceAgent.Win
{
    internal class Program
    {
        private const string ConfigPath = "voice_agent_config.txt";
        private const string KeywordFile = "keywords.json";

        // ==========================================
        // 【魔法 API】用來動態連接終端機，解決 WinExe 沒有輸出的問題
        // ==========================================
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        public static void Main(string[] args)
        {
            // 如果使用者有帶參數 (代表他在用 CLI 模式)
            if (args.Length > 0)
            {
                // 動態連接到呼叫此程式的 CMD/PowerShell 視窗
                AttachConsole(ATTACH_PARENT_PROCESS);
                Console.WriteLine(); // 先印一個換行，避免跟使用者的輸入黏在一起

                string command = args[0].ToLower();

                if (command == "-help" || command == "--help")
                {
                    Console.WriteLine("==================================================");
                    Console.WriteLine("📖 VoiceAgent 指令說明:");
                    Console.WriteLine("  VoiceAgent.exe -key <路徑>              : 綁定 Google STT 金鑰");
                    Console.WriteLine("  VoiceAgent.exe -addword <詞> <替換文字> : 新增自訂關鍵字");
                    Console.WriteLine("  VoiceAgent.exe -delword <詞>            : 刪除自訂關鍵字");
                    Console.WriteLine("  VoiceAgent.exe -listwords               : 顯示所有自訂關鍵字");
                    Console.WriteLine("==================================================");
                    return; 
                }
                else if (command == "-key" || command == "--key")
                {
                    if (args.Length > 1)
                    {
                        string keyPath = args[1].Replace("\"", "").Replace("'", "").Trim();
                        if (File.Exists(keyPath) && keyPath.EndsWith(".json"))
                        {
                            File.WriteAllText(ConfigPath, keyPath);
                            Console.WriteLine($"✅ 金鑰已成功綁定: {keyPath}");
                        }
                        else Console.WriteLine($"❌ 錯誤：找不到檔案 '{keyPath}'。");
                    }
                    return;
                }
                else if (command == "-addword" || command == "--addword")
                {
                    if (args.Length > 2)
                    {
                        var dict = LoadKeywords();
                        dict[args[1]] = args[2];
                        SaveKeywords(dict);
                        Console.WriteLine($"✅ 已儲存關鍵字: '{args[1]}' -> '{args[2]}'");
                    }
                    else Console.WriteLine("❌ 錯誤：參數不足。範例: VoiceAgent.exe -addword 我的地址 台北市信義區");
                    return;
                }
                else if (command == "-delword" || command == "--delword")
                {
                    if (args.Length > 1)
                    {
                        var dict = LoadKeywords();
                        if (dict.Remove(args[1]))
                        {
                            SaveKeywords(dict);
                            Console.WriteLine($"🗑️ 已刪除關鍵字: '{args[1]}'");
                        }
                        else Console.WriteLine($"⚠️ 找不到關鍵字: '{args[1]}'");
                    }
                    return;
                }
                else if (command == "-listwords" || command == "--listwords")
                {
                    var dict = LoadKeywords();
                    Console.WriteLine("📋 自訂關鍵字列表:");
                    if (dict.Count == 0) Console.WriteLine("  (目前沒有設定任何關鍵字)");
                    foreach (var kvp in dict) Console.WriteLine($"  - {kvp.Key} ➔ {kvp.Value}");
                    return;
                }
            }

            // 如果沒有帶參數，代表是「雙擊啟動」，直接啟動隱形的 GUI，完全不會有黑畫面！
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
    }
}