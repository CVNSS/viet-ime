// Chạy: dotnet run TestHook.cs
// Test đơn giản keyboard hook

using System.Diagnostics;
using System.Runtime.InteropServices;

class Program
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    
    private static IntPtr _hookId = IntPtr.Zero;
    private static LowLevelKeyboardProc _proc = HookCallback; // QUAN TRỌNG: giữ reference
    
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
    
    static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== TEST KEYBOARD HOOK ===");
        Console.WriteLine("Nhấn bất kỳ phím nào để test...");
        Console.WriteLine("Nhấn ESC để thoát.");
        Console.WriteLine();
        
        // Cài đặt hook
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        
        Console.WriteLine($"Module: {curModule.ModuleName}");
        
        var moduleHandle = GetModuleHandle(curModule.ModuleName);
        Console.WriteLine($"Module Handle: {moduleHandle}");
        
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, moduleHandle, 0);
        
        if (_hookId == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Console.WriteLine($"FAILED! SetWindowsHookEx error: {error}");
            Console.ReadKey();
            return;
        }
        
        Console.WriteLine($"Hook ID: {_hookId}");
        Console.WriteLine("Hook installed successfully!");
        Console.WriteLine();
        
        // Message loop đơn giản
        Console.WriteLine("Waiting for keys... (Nhấn ESC để thoát)");
        
        while (true)
        {
            // Đọc key từ console (để giữ app chạy)
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape)
                    break;
            }
            Thread.Sleep(10);
        }
        
        // Gỡ hook
        UnhookWindowsHookEx(_hookId);
        Console.WriteLine("Hook uninstalled.");
    }
    
    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
        {
            var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            char ch = (char)hookStruct.vkCode;
            Console.WriteLine($"[HOOK] VK={hookStruct.vkCode}, Char='{ch}', Scan={hookStruct.scanCode}");
        }
        
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }
}
