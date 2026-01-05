using System.Diagnostics;
using System.Runtime.InteropServices;
using VietIME.Core.Engines;

namespace VietIME.Hook;

/// <summary>
/// Quản lý keyboard hook - bắt tất cả phím trên toàn hệ thống
/// Hoạt động với mọi ứng dụng Windows: Office, Browser, IDE, etc.
/// </summary>
public class KeyboardHook : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    
    // QUAN TRỌNG: Khai báo delegate như field và khởi tạo ngay để tránh GC
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    
    private IInputEngine? _engine;
    private bool _isEnabled = true;
    private bool _disposed = false;
    
    // Flag để tránh xử lý lại input do chính mình gửi
    private bool _isSendingInput = false;
    
    // Extra info marker để nhận diện input từ VietIME
    private static readonly UIntPtr VIET_IME_MARKER = new(0x56494D45); // "VIME" in hex
    
    // Chế độ gửi input - true = dùng Clipboard (cho terminal), false = dùng SendInput
    private bool _useClipboardMethod = true; // Mặc định dùng clipboard cho terminal
    
    // Timeout reset buffer (milliseconds) - nếu không gõ trong khoảng này, buffer sẽ reset
    private const int BUFFER_TIMEOUT_MS = 2000; // 2 giây
    private DateTime _lastKeyTime = DateTime.MinValue;
    
    // Debounce cho hotkey toggle - tránh toggle nhiều lần
    private DateTime _lastToggleTime = DateTime.MinValue;
    private const int TOGGLE_DEBOUNCE_MS = 300;
    
    public KeyboardHook()
    {
        // Khởi tạo delegate trong constructor để giữ reference
        _proc = HookCallback;
    }
    
    /// <summary>
    /// Event khi trạng thái IME thay đổi
    /// </summary>
    public event EventHandler<bool>? EnabledChanged;
    
    /// <summary>
    /// Event khi có lỗi xảy ra
    /// </summary>
    public event EventHandler<string>? Error;
    
    /// <summary>
    /// Trạng thái bật/tắt IME
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                _engine?.Reset();
                EnabledChanged?.Invoke(this, value);
            }
        }
    }
    
    /// <summary>
    /// Engine đang sử dụng
    /// </summary>
    public IInputEngine? Engine
    {
        get => _engine;
        set
        {
            _engine?.Reset();
            _engine = value;
        }
    }
    
    /// <summary>
    /// Cài đặt hook
    /// </summary>
    public void Install()
    {
        if (_hookId != IntPtr.Zero)
            return;
        
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        
        if (curModule == null)
        {
            Error?.Invoke(this, "Không thể lấy module handle");
            return;
        }
        
        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName),
            0);
        
        if (_hookId == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Error?.Invoke(this, $"Không thể cài đặt hook. Error code: {error}");
        }
    }
    
    /// <summary>
    /// Gỡ bỏ hook
    /// </summary>
    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Event debug
    /// </summary>
    public event Action<string>? DebugLog;
    
    /// <summary>
    /// Chế độ gửi input - true = Clipboard (tốt cho terminal), false = SendInput (nhanh hơn)
    /// </summary>
    public bool UseClipboardMethod
    {
        get => _useClipboardMethod;
        set => _useClipboardMethod = value;
    }
    
    /// <summary>
    /// Callback xử lý mỗi phím được nhấn
    /// </summary>
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            // Nếu đang gửi input hoặc nCode < 0, bỏ qua
            if (nCode < 0 || _isSendingInput)
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            
            // Bỏ qua input do chính mình gửi (so sánh giá trị)
            if ((ulong)hookStruct.dwExtraInfo == (ulong)VIET_IME_MARKER)
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            // Chỉ xử lý key down
            int msg = wParam.ToInt32();
            if (msg != NativeMethods.WM_KEYDOWN && msg != NativeMethods.WM_SYSKEYDOWN)
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            // Xử lý hotkey toggle (Ctrl + Shift)
            if (HandleToggleHotkey(hookStruct.vkCode))
            {
                return (IntPtr)1; // Chặn phím
            }
            
            // Nếu IME tắt, bỏ qua
            if (!_isEnabled || _engine == null)
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            // Bỏ qua nếu Ctrl hoặc Alt được nhấn (shortcuts)
            // QUAN TRỌNG: Không reset buffer ở đây vì có thể là Ctrl từ Ctrl+V clipboard
            if (NativeMethods.IsCtrlPressed() || NativeMethods.IsAltPressed())
            {
                // Chỉ skip processing, KHÔNG reset buffer
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            // Xử lý các phím đặc biệt
            if (HandleSpecialKey(hookStruct.vkCode))
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            // Kiểm tra timeout - nếu quá lâu không gõ, reset buffer
            var now = DateTime.UtcNow;
            if (_lastKeyTime != DateTime.MinValue && 
                (now - _lastKeyTime).TotalMilliseconds > BUFFER_TIMEOUT_MS)
            {
                _engine?.Reset();
            }
            _lastKeyTime = now;
            
            // Chuyển virtual key thành ký tự
            char? ch = NativeMethods.VirtualKeyToChar(hookStruct.vkCode, hookStruct.scanCode);
            
            DebugLog?.Invoke($"Key: vk={hookStruct.vkCode}, char={ch}");
            
            if (!ch.HasValue)
            {
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            
            // Xử lý ký tự qua engine
            bool isShiftPressed = NativeMethods.IsShiftPressed();
            var result = _engine.ProcessKey(ch.Value, isShiftPressed);
            
            DebugLog?.Invoke($"Engine result: Handled={result.Handled}, Output={result.OutputText}, Backspace={result.BackspaceCount}, Buffer={result.CurrentBuffer}");
            
            if (result.Handled && result.OutputText != null)
            {
                if (_useClipboardMethod)
                {
                    // Phương pháp Clipboard - hoạt động tốt với Terminal/PowerShell
                    SendViaClipboard(result.BackspaceCount, result.OutputText);
                }
                else
                {
                    // Phương pháp SendInput - nhanh hơn nhưng không hỗ trợ terminal
                    SendBackspaces(result.BackspaceCount);
                    SendUnicodeString(result.OutputText);
                }
                
                return (IntPtr)1; // Chặn phím gốc
            }
            
            // Nếu không xử lý đặc biệt, thêm vào buffer và cho phím đi qua
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke($"Error: {ex.Message}");
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }
    
    /// <summary>
    /// Xử lý hotkey toggle IME (Ctrl + Shift hoặc Ctrl + `)
    /// </summary>
    private bool HandleToggleHotkey(uint vkCode)
    {
        // Phương pháp 1: Ctrl + ` (backtick/grave) - ưu tiên
        if (vkCode == 0xC0 && NativeMethods.IsCtrlPressed() && !NativeMethods.IsShiftPressed())
        {
            return TryToggle();
        }
        
        // Phương pháp 2: Ctrl + Shift (khi thả Ctrl trong khi giữ Shift)
        // Chỉ trigger khi nhấn Shift và Ctrl đang giữ
        if (vkCode == NativeMethods.VK_SHIFT && NativeMethods.IsCtrlPressed())
        {
            return TryToggle();
        }
        
        return false;
    }
    
    /// <summary>
    /// Toggle với debounce để tránh toggle nhiều lần
    /// </summary>
    private bool TryToggle()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastToggleTime).TotalMilliseconds < TOGGLE_DEBOUNCE_MS)
        {
            return true; // Vẫn chặn phím nhưng không toggle
        }
        
        _lastToggleTime = now;
        IsEnabled = !IsEnabled;
        return true;
    }
    
    /// <summary>
    /// Xử lý các phím đặc biệt (Space, Enter, Backspace, etc.)
    /// </summary>
    private bool HandleSpecialKey(uint vkCode)
    {
        switch (vkCode)
        {
            case NativeMethods.VK_SPACE:
            case NativeMethods.VK_RETURN:
            case NativeMethods.VK_TAB:
            case NativeMethods.VK_ESCAPE:
                _engine?.Reset();
                return true;
            
            case NativeMethods.VK_BACK:
                _engine?.ProcessBackspace();
                return true;
            
            // Phím di chuyển con trỏ - reset buffer vì không thể theo dõi vị trí mới
            case NativeMethods.VK_LEFT:
            case NativeMethods.VK_RIGHT:
            case NativeMethods.VK_UP:
            case NativeMethods.VK_DOWN:
            case NativeMethods.VK_HOME:
            case NativeMethods.VK_END:
            case NativeMethods.VK_PRIOR:  // Page Up
            case NativeMethods.VK_NEXT:   // Page Down
            case NativeMethods.VK_DELETE:
                _engine?.Reset();
                return true;
            
            default:
                return false;
        }
    }
    
    /// <summary>
    /// Gửi n phím Backspace
    /// </summary>
    private void SendBackspaces(int count)
    {
        if (count <= 0) return;
        
        _isSendingInput = true;
        
        try
        {
            var inputs = new NativeMethods.INPUT[count * 2];
            
            for (int i = 0; i < count; i++)
            {
                // Key down
                inputs[i * 2] = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = NativeMethods.VK_BACK,
                            wScan = 0,
                            dwFlags = 0,
                            time = 0,
                            dwExtraInfo = VIET_IME_MARKER
                        }
                    }
                };
                
                // Key up
                inputs[i * 2 + 1] = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = NativeMethods.VK_BACK,
                            wScan = 0,
                            dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = VIET_IME_MARKER
                        }
                    }
                };
            }
            
            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            DebugLog?.Invoke($"SendBackspaces: count={count}, sent={sent}");
        }
        finally
        {
            _isSendingInput = false;
        }
    }
    
    /// <summary>
    /// Gửi qua Clipboard - Phương pháp này hoạt động với Terminal/PowerShell
    /// </summary>
    private void SendViaClipboard(int backspaceCount, string text)
    {
        _isSendingInput = true;
        
        try
        {
            // 1. Gửi backspace để xóa ký tự cũ
            if (backspaceCount > 0)
            {
                var bsInputs = new NativeMethods.INPUT[backspaceCount * 2];
                for (int i = 0; i < backspaceCount; i++)
                {
                    bsInputs[i * 2] = new NativeMethods.INPUT
                    {
                        type = NativeMethods.INPUT_KEYBOARD,
                        u = new NativeMethods.INPUTUNION
                        {
                            ki = new NativeMethods.KEYBDINPUT
                            {
                                wVk = NativeMethods.VK_BACK,
                                wScan = 0,
                                dwFlags = 0,
                                time = 0,
                                dwExtraInfo = VIET_IME_MARKER
                            }
                        }
                    };
                    bsInputs[i * 2 + 1] = new NativeMethods.INPUT
                    {
                        type = NativeMethods.INPUT_KEYBOARD,
                        u = new NativeMethods.INPUTUNION
                        {
                            ki = new NativeMethods.KEYBDINPUT
                            {
                                wVk = NativeMethods.VK_BACK,
                                wScan = 0,
                                dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                                time = 0,
                                dwExtraInfo = VIET_IME_MARKER
                            }
                        }
                    };
                }
                NativeMethods.SendInput((uint)bsInputs.Length, bsInputs, Marshal.SizeOf<NativeMethods.INPUT>());
                
                // Chờ để backspace được xử lý hoàn toàn (tăng từ 5ms lên 15ms)
                Thread.Sleep(15);
            }
            
            // 2. Set text vào clipboard sử dụng Win32 API
            SetClipboardText(text);
            
            // 3. Gửi Ctrl+V để paste
            var pasteInputs = new NativeMethods.INPUT[4];
            
            // Ctrl down
            pasteInputs[0] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = NativeMethods.VK_CONTROL,
                        wScan = 0,
                        dwFlags = 0,
                        time = 0,
                        dwExtraInfo = VIET_IME_MARKER
                    }
                }
            };
            
            // V down
            pasteInputs[1] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0x56, // V key
                        wScan = 0,
                        dwFlags = 0,
                        time = 0,
                        dwExtraInfo = VIET_IME_MARKER
                    }
                }
            };
            
            // V up
            pasteInputs[2] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0x56,
                        wScan = 0,
                        dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = VIET_IME_MARKER
                    }
                }
            };
            
            // Ctrl up
            pasteInputs[3] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = NativeMethods.VK_CONTROL,
                        wScan = 0,
                        dwFlags = NativeMethods.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = VIET_IME_MARKER
                    }
                }
            };
            
            uint sent = NativeMethods.SendInput(4, pasteInputs, Marshal.SizeOf<NativeMethods.INPUT>());
            DebugLog?.Invoke($"SendViaClipboard: backspace={backspaceCount}, text='{text}', paste_sent={sent}");
            
            // Chờ để paste hoàn tất trước khi xử lý phím tiếp theo
            Thread.Sleep(10);
        }
        finally
        {
            _isSendingInput = false;
        }
    }
    
    /// <summary>
    /// Set text vào clipboard sử dụng Win32 API
    /// </summary>
    private bool SetClipboardText(string text)
    {
        // Retry một vài lần nếu clipboard bị lock
        for (int retry = 0; retry < 5; retry++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    NativeMethods.EmptyClipboard();
                    
                    // Allocate memory cho Unicode string (bao gồm null terminator)
                    int byteCount = (text.Length + 1) * 2;
                    IntPtr hGlobal = NativeMethods.GlobalAlloc(
                        NativeMethods.GMEM_MOVEABLE, 
                        (UIntPtr)byteCount);
                    
                    if (hGlobal == IntPtr.Zero)
                    {
                        DebugLog?.Invoke("SetClipboard: GlobalAlloc failed");
                        return false;
                    }
                    
                    IntPtr pGlobal = NativeMethods.GlobalLock(hGlobal);
                    if (pGlobal == IntPtr.Zero)
                    {
                        NativeMethods.GlobalFree(hGlobal);
                        DebugLog?.Invoke("SetClipboard: GlobalLock failed");
                        return false;
                    }
                    
                    // Copy string vào memory
                    Marshal.Copy(text.ToCharArray(), 0, pGlobal, text.Length);
                    // Thêm null terminator
                    Marshal.WriteInt16(pGlobal + text.Length * 2, 0);
                    
                    NativeMethods.GlobalUnlock(hGlobal);
                    
                    // Set clipboard data
                    IntPtr result = NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal);
                    
                    if (result == IntPtr.Zero)
                    {
                        NativeMethods.GlobalFree(hGlobal);
                        DebugLog?.Invoke("SetClipboard: SetClipboardData failed");
                        return false;
                    }
                    
                    // Không free hGlobal - clipboard sẽ tự quản lý
                    return true;
                }
                finally
                {
                    NativeMethods.CloseClipboard();
                }
            }
            
            Thread.Sleep(10);
        }
        
        DebugLog?.Invoke("SetClipboard: OpenClipboard failed after retries");
        return false;
    }
    
    /// <summary>
    /// Gửi chuỗi Unicode đến ứng dụng đang active (không hỗ trợ terminal)
    /// </summary>
    private void SendUnicodeString(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        
        _isSendingInput = true;
        
        try
        {
            var inputs = new NativeMethods.INPUT[text.Length * 2];
            
            for (int i = 0; i < text.Length; i++)
            {
                ushort unicode = text[i];
                
                // Key down (Unicode)
                inputs[i * 2] = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = unicode,
                            dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                            time = 0,
                            dwExtraInfo = VIET_IME_MARKER
                        }
                    }
                };
                
                // Key up (Unicode)
                inputs[i * 2 + 1] = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = 0,
                            wScan = unicode,
                            dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = VIET_IME_MARKER
                        }
                    }
                };
            }
            
            uint sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            DebugLog?.Invoke($"SendUnicode: text='{text}', inputs={inputs.Length}, sent={sent}");
            
            if (sent == 0)
            {
                int error = Marshal.GetLastWin32Error();
                DebugLog?.Invoke($"SendInput ERROR: {error}");
            }
        }
        finally
        {
            _isSendingInput = false;
        }
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            Uninstall();
            _disposed = true;
        }
    }
    
    ~KeyboardHook()
    {
        Dispose(false);
    }
}
