using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VoiceAgent.Win
{
    public static class WindowsInputInjector
    {
        [DllImport("user32.dll")] private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        private const byte VK_CONTROL = 0x11; private const byte VK_A = 0x41; private const byte VK_C = 0x43; private const byte VK_V = 0x56;
        private const byte VK_RIGHT = 0x27; private const byte VK_BACK = 0x08;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static void PressCombo(byte key)
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private static void PressKey(byte key)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static async Task<string> ExtractCurrentTextAsync()
        {
            await TextCopy.ClipboardService.SetTextAsync("");
            PressCombo(VK_A); // Ctrl+A
            await Task.Delay(50); 
            PressCombo(VK_C); // Ctrl+C
            await Task.Delay(100); 
            PressKey(VK_RIGHT); // 取消全選
            return (await TextCopy.ClipboardService.GetTextAsync() ?? "").Trim();
        }

        public static async Task AppendTextAtEndAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            await TextCopy.ClipboardService.SetTextAsync(text);
            await Task.Delay(50);
            PressCombo(VK_A);     // 全選
            await Task.Delay(20);
            PressKey(VK_RIGHT);   // 移至末尾
            await Task.Delay(20);
            PressCombo(VK_V);     // 貼上
        }

        public static async Task ReplaceEntireTextAsync(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                PressCombo(VK_A);
                await Task.Delay(50);
                PressKey(VK_BACK); // Backspace 刪除
            }
            else
            {
                await TextCopy.ClipboardService.SetTextAsync(text);
                await Task.Delay(50);
                PressCombo(VK_A);
                await Task.Delay(50);
                PressCombo(VK_V);
            }
        }
    }
}