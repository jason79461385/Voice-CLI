using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace VoiceAgent.Win
{
    public static class NativeInputInjector
    {
        // ==========================================
        // Win32 API 宣告
        // ==========================================
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_SHOW = 5;

        // 🌟 關鍵修正：補齊 64-bit 完整的結構大小，確保 Size 剛好是 40 bytes！
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT 
        { 
            public uint type; 
            public InputUnion U; 
            public static int Size => Marshal.SizeOf(typeof(INPUT)); 
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion 
        { 
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki; 
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT 
        { 
            public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; 
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg; public ushort wParamL; public ushort wParamH;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;
        
        private const ushort VK_BACK = 0x08;
        private const ushort VK_CONTROL = 0x11; // 🌟 改用泛用型 Ctrl
        private const ushort VK_V = 0x56;
        private const ushort VK_A = 0x41;

        // ==========================================
        // 🛡️ 焦點切換
        // ==========================================
        public static void ForceForegroundWindow(IntPtr hWnd)
        {
            if (!IsWindow(hWnd)) return;

            IntPtr currentForeground = GetForegroundWindow();
            uint targetThreadId = GetWindowThreadProcessId(hWnd, out _);
            uint foregroundThreadId = GetWindowThreadProcessId(currentForeground, out _);
            uint currentThreadId = GetCurrentThreadId();

            bool attached = false;
            if (foregroundThreadId != currentThreadId)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, true);
                attached = true;
            }

            ShowWindow(hWnd, SW_SHOW);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);

            if (attached) AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }

        // ==========================================
        // 🗡️ Chrome 剋星：硬體級 Ctrl+V 貼上
        // ==========================================
        public static async Task PasteFromClipboardAsync()
        {
            var inputDown = new INPUT[2];
            inputDown[0] = new INPUT { type = INPUT_KEYBOARD }; inputDown[0].U.ki.wVk = VK_CONTROL;
            inputDown[1] = new INPUT { type = INPUT_KEYBOARD }; inputDown[1].U.ki.wVk = VK_V;
            
            // 🌟 加入偵測：如果回傳值不是 2，代表 Windows 拒絕了我們的輸入！
            uint resDown = SendInput(2, inputDown, INPUT.Size);
            Console.WriteLine($"[Debug] SendInput Ctrl+V Down 執行結果: {resDown} (應該要是 2)");

            await Task.Delay(30);

            var inputUp = new INPUT[2];
            inputUp[0] = new INPUT { type = INPUT_KEYBOARD }; inputUp[0].U.ki.wVk = VK_V; inputUp[0].U.ki.dwFlags = KEYEVENTF_KEYUP;
            inputUp[1] = new INPUT { type = INPUT_KEYBOARD }; inputUp[1].U.ki.wVk = VK_CONTROL; inputUp[1].U.ki.dwFlags = KEYEVENTF_KEYUP;
            
            uint resUp = SendInput(2, inputUp, INPUT.Size);
            Console.WriteLine($"[Debug] SendInput Ctrl+V Up 執行結果: {resUp} (應該要是 2)");
            
            await Task.Delay(10);
        }

        // ==========================================
        // 🗡️ 全選並貼上 (用於全域替換)
        // ==========================================
        public static async Task SelectAllThenPasteAsync()
        {
            var inputDownA = new INPUT[2];
            inputDownA[0] = new INPUT { type = INPUT_KEYBOARD }; inputDownA[0].U.ki.wVk = VK_CONTROL;
            inputDownA[1] = new INPUT { type = INPUT_KEYBOARD }; inputDownA[1].U.ki.wVk = VK_A;
            SendInput(2, inputDownA, INPUT.Size);

            await Task.Delay(30);

            var inputUpA = new INPUT[2];
            inputUpA[0] = new INPUT { type = INPUT_KEYBOARD }; inputUpA[0].U.ki.wVk = VK_A; inputUpA[0].U.ki.dwFlags = KEYEVENTF_KEYUP;
            inputUpA[1] = new INPUT { type = INPUT_KEYBOARD }; inputUpA[1].U.ki.wVk = VK_CONTROL; inputUpA[1].U.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput(2, inputUpA, INPUT.Size);

            await Task.Delay(50);
            await PasteFromClipboardAsync();
        }

        // ==========================================
        // 🗡️ 其他保留武器 (Unicode / 退格)
        // ==========================================
        public static async Task TypeStringAsync(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var inputs = new INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++) {
                inputs[i * 2] = new INPUT { type = INPUT_KEYBOARD };
                inputs[i * 2].U.ki.wScan = text[i];
                inputs[i * 2].U.ki.dwFlags = KEYEVENTF_UNICODE;
                inputs[i * 2 + 1] = new INPUT { type = INPUT_KEYBOARD };
                inputs[i * 2 + 1].U.ki.wScan = text[i];
                inputs[i * 2 + 1].U.ki.dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP;
            }
            SendInput((uint)inputs.Length, inputs, INPUT.Size);
            await Task.Delay(5);
        }

        public static async Task SendBackspacesAsync(int count)
        {
            if (count <= 0) return;
            var inputs = new INPUT[count * 2];
            for (int i = 0; i < count; i++) {
                inputs[i * 2] = new INPUT { type = INPUT_KEYBOARD }; inputs[i * 2].U.ki.wVk = VK_BACK;
                inputs[i * 2 + 1] = new INPUT { type = INPUT_KEYBOARD }; inputs[i * 2 + 1].U.ki.wVk = VK_BACK; inputs[i * 2 + 1].U.ki.dwFlags = KEYEVENTF_KEYUP;
            }
            SendInput((uint)inputs.Length, inputs, INPUT.Size);
            await Task.Delay(15);
        }
    }
}