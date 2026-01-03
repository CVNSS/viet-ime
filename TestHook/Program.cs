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
    
    // Message loop functions
    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);
    
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);
    
    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }
    
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
        
        // QUAN TRỌNG: Cần Windows message loop!
        Console.WriteLine("Waiting for keys... (Nhấn Ctrl+C để thoát)");
        Console.WriteLine("Gõ bất kỳ phím nào trong bất kỳ ứng dụng nào...");
        Console.WriteLine();
        
        // Windows message loop
        while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
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
