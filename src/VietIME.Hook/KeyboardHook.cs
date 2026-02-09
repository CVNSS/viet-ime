using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using VietIME.Core.Engines;

namespace VietIME.Hook;

/// <summary>
/// Quản lý keyboard hook - bắt tất cả phím trên toàn hệ thống
///
/// Chiến lược v7 - TỐI ƯU TỐC ĐỘ:
/// - Background thread gửi output → hook callback return ngay → không block gõ phím
/// - Queue phím khi busy → xử lý đúng thứ tự sau khi output gửi xong
/// - Bỏ save/restore clipboard → tiết kiệm ~45ms mỗi phím
/// - Delay 30ms giữa backspace và paste (vừa đủ cho terminal)
/// - Tách riêng backspace và paste thành 2 SendInput (đảm bảo thứ tự)
/// </summary>
public class KeyboardHook : IDisposable
{
    private IntPtr _hookId = IntPtr.Zero;
    private readonly NativeMethods.LowLevelKeyboardProc _proc;
    private IInputEngine? _engine;
    private bool _isEnabled = true;
    private bool _disposed = false;
    private volatile bool _isSendingInput = false;
    private volatile bool _isBusySending = false;

    private static readonly UIntPtr VIET_IME_MARKER = new(0x56494D45);

    private bool _useClipboardMethod = true;
    private bool _useSelectReplace = true;
    private const int BUFFER_TIMEOUT_MS = 2000;
    private DateTime _lastKeyTime = DateTime.MinValue;
    private DateTime _lastToggleTime = DateTime.MinValue;
    private const int TOGGLE_DEBOUNCE_MS = 300;

    // Queue phím chờ xử lý khi đang busy
    private readonly ConcurrentQueue<PendingKey> _pendingKeys = new();
    private record PendingKey(char Char, bool IsShift);

    public KeyboardHook()
    {
        _proc = HookCallback;
    }

    public event EventHandler<bool>? EnabledChanged;
    public event EventHandler<string>? Error;
    public event Action<string>? DebugLog;

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

    public IInputEngine? Engine
    {
        get => _engine;
        set
        {
            _engine?.Reset();
            _engine = value;
        }
    }

    public bool UseClipboardMethod
    {
        get => _useClipboardMethod;
        set => _useClipboardMethod = value;
    }

    public void Install()
    {
        if (_hookId != IntPtr.Zero) return;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;

        if (curModule == null)
        {
            Error?.Invoke(this, "Không thể lấy module handle");
            return;
        }

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _proc,
            NativeMethods.GetModuleHandle(curModule.ModuleName), 0);

        if (_hookId == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            Error?.Invoke(this, $"Không thể cài đặt hook. Error code: {error}");
        }
    }

    public void Uninstall()
    {
        if (_hookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode < 0 || _isSendingInput)
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

            var hookStruct = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            bool isShiftPressed = NativeMethods.IsShiftPressed(); // Bắt NGAY trước khi bị mất

            if ((ulong)hookStruct.dwExtraInfo == (ulong)VIET_IME_MARKER)
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            if (msg != NativeMethods.WM_KEYDOWN && msg != NativeMethods.WM_SYSKEYDOWN)
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (HandleToggleHotkey(hookStruct.vkCode))
                return (IntPtr)1;

            if (!_isEnabled || _engine == null)
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (NativeMethods.IsCtrlPressed() || NativeMethods.IsAltPressed())
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (HandleSpecialKey(hookStruct.vkCode))
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

            // Timeout check
            var now = DateTime.UtcNow;
            if (_lastKeyTime != DateTime.MinValue &&
                (now - _lastKeyTime).TotalMilliseconds > BUFFER_TIMEOUT_MS)
            {
                _engine.Reset();
            }
            _lastKeyTime = now;

            char? ch = NativeMethods.VirtualKeyToChar(hookStruct.vkCode, hookStruct.scanCode, isShiftPressed);
            if (!ch.HasValue)
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);

            // Nếu đang busy gửi output, queue phím lại
            if (_isBusySending)
            {
                _pendingKeys.Enqueue(new PendingKey(ch.Value, isShiftPressed));
                return (IntPtr)1; // Chặn phím - sẽ xử lý sau
            }

            // Xử lý ký tự qua engine
            var result = _engine.ProcessKey(ch.Value, isShiftPressed);

            DebugLog?.Invoke($"Key '{ch.Value}': Handled={result.Handled}, Output='{result.OutputText}', BS={result.BackspaceCount}");

            if (result.Handled && result.OutputText != null)
            {
                // Gửi output trên background thread → hook return ngay → không block gõ phím
                _isBusySending = true;
                int bs = result.BackspaceCount;
                string text = result.OutputText;

                Task.Run(() =>
                {
                    try
                    {
                        if (_useClipboardMethod)
                        {
                            if (_useSelectReplace)
                                SendViaSelectReplace(bs, text);
                            else
                                SendViaClipboard(bs, text);
                        }
                        else
                        {
                            SendBackspaces(bs);
                            SendUnicodeString(text);
                        }
                    }
                    finally
                    {
                        ProcessPendingKeys();       // Xử lý queue TRƯỚC
                        _isBusySending = false;     // Mở khóa SAU → tránh race condition
                    }
                });

                return (IntPtr)1; // Chặn phím gốc
            }

            // Engine không xử lý → phím đi qua bình thường
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            DebugLog?.Invoke($"Error: {ex.Message}");
            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
        }
    }

    /// <summary>
    /// Xử lý phím đã queue khi đang busy
    /// Chạy trên background thread sau khi output gửi xong
    /// </summary>
    private void ProcessPendingKeys()
    {
        while (_pendingKeys.TryDequeue(out var pending))
        {
            if (_engine == null) break;

            var result = _engine.ProcessKey(pending.Char, pending.IsShift);

            if (result.Handled && result.OutputText != null)
            {
                if (_useClipboardMethod)
                {
                    if (_useSelectReplace)
                        SendViaSelectReplace(result.BackspaceCount, result.OutputText);
                    else
                        SendViaClipboard(result.BackspaceCount, result.OutputText);
                }
                else
                {
                    SendBackspaces(result.BackspaceCount);
                    SendUnicodeString(result.OutputText);
                }
            }
            else
            {
                // Engine không xử lý → gửi ký tự gốc
                SendCharDirectly(pending.Char);
            }

            // Chờ ứng dụng đích xử lý xong trước khi gửi phím tiếp
            Thread.Sleep(50);
        }
    }

    /// <summary>
    /// Gửi 1 ký tự trực tiếp (cho phím đã bị chặn nhưng engine không xử lý)
    /// </summary>
    private void SendCharDirectly(char ch)
    {
        _isSendingInput = true;
        try
        {
            ushort unicode = ch;
            var inputs = new NativeMethods.INPUT[2];
            inputs[0] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0, wScan = unicode,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                        time = 0, dwExtraInfo = VIET_IME_MARKER
                    }
                }
            };
            inputs[1] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.INPUTUNION
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0, wScan = unicode,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP,
                        time = 0, dwExtraInfo = VIET_IME_MARKER
                    }
                }
            };
            NativeMethods.SendInput(2, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }
        finally
        {
            _isSendingInput = false;
        }
    }

    private bool HandleToggleHotkey(uint vkCode)
    {
        if (vkCode == 0xC0 && NativeMethods.IsCtrlPressed() && !NativeMethods.IsShiftPressed())
            return TryToggle();
        if (vkCode == NativeMethods.VK_SHIFT && NativeMethods.IsCtrlPressed())
            return TryToggle();
        return false;
    }

    private bool TryToggle()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastToggleTime).TotalMilliseconds < TOGGLE_DEBOUNCE_MS)
            return true;
        _lastToggleTime = now;
        IsEnabled = !IsEnabled;
        return true;
    }

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
            case NativeMethods.VK_LEFT:
            case NativeMethods.VK_RIGHT:
            case NativeMethods.VK_UP:
            case NativeMethods.VK_DOWN:
            case NativeMethods.VK_HOME:
            case NativeMethods.VK_END:
            case NativeMethods.VK_PRIOR:
            case NativeMethods.VK_NEXT:
            case NativeMethods.VK_DELETE:
                _engine?.Reset();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Gửi qua Select+Replace (Shift+Left * N rồi Ctrl+V)
    ///
    /// Ưu điểm so với Backspace+Paste:
    /// - Paste thay thế selection là atomic operation
    /// - Không bị tình trạng backspace chưa xong mà paste đã đến (lỗi Chrome)
    /// - Hoạt động đúng trên Chrome, Edge, và các Chromium-based browsers
    /// </summary>
    private void SendViaSelectReplace(int backspaceCount, string text)
    {
        _isSendingInput = true;
        try
        {
            // 1. Set clipboard
            if (!SetClipboardText(text))
            {
                DebugLog?.Invoke("SendViaSelectReplace: SetClipboardText failed");
                return;
            }

            // 2. Select N ký tự bằng Shift+Left (thay vì backspace)
            if (backspaceCount > 0)
            {
                // Shift down
                var shiftDown = MakeKeyInput(NativeMethods.VK_SHIFT, false);
                NativeMethods.SendInput(1, new[] { shiftDown }, Marshal.SizeOf<NativeMethods.INPUT>());

                // Left * N (với Shift giữ → select)
                var leftInputs = new NativeMethods.INPUT[backspaceCount * 2];
                for (int i = 0; i < backspaceCount; i++)
                {
                    leftInputs[i * 2] = MakeKeyInput((int)NativeMethods.VK_LEFT, false);
                    leftInputs[i * 2 + 1] = MakeKeyInput((int)NativeMethods.VK_LEFT, true);
                }
                NativeMethods.SendInput((uint)leftInputs.Length, leftInputs, Marshal.SizeOf<NativeMethods.INPUT>());

                // Shift up
                var shiftUp = MakeKeyInput(NativeMethods.VK_SHIFT, true);
                NativeMethods.SendInput(1, new[] { shiftUp }, Marshal.SizeOf<NativeMethods.INPUT>());

                // Chờ selection hoàn tất
                Thread.Sleep(30);
            }

            // 3. Gửi Ctrl+V để paste thay thế selection
            var pasteInputs = new NativeMethods.INPUT[4];
            pasteInputs[0] = MakeKeyInput(NativeMethods.VK_CONTROL, false);
            pasteInputs[1] = MakeKeyInput(0x56, false);
            pasteInputs[2] = MakeKeyInput(0x56, true);
            pasteInputs[3] = MakeKeyInput(NativeMethods.VK_CONTROL, true);
            NativeMethods.SendInput(4, pasteInputs, Marshal.SizeOf<NativeMethods.INPUT>());

            DebugLog?.Invoke($"SendViaSelectReplace: select={backspaceCount}, text='{text}'");

            // 4. Chờ paste xong
            Thread.Sleep(30);
        }
        finally
        {
            _isSendingInput = false;
        }
    }

    /// <summary>
    /// Gửi qua Clipboard (fallback method)
    ///
    /// Tối ưu v7:
    /// - Chạy trên background thread (hook callback đã return)
    /// - Không save/restore clipboard (tiết kiệm ~45ms)
    /// - Tách backspace và paste riêng, Sleep 30ms ở giữa
    /// - Tổng delay: ~40ms (vs ~115ms ở v6)
    /// </summary>
    private void SendViaClipboard(int backspaceCount, string text)
    {
        _isSendingInput = true;
        try
        {
            // 1. Set clipboard
            if (!SetClipboardText(text))
            {
                DebugLog?.Invoke("SendViaClipboard: SetClipboardText failed");
                return;
            }

            // 2. Gửi backspace
            if (backspaceCount > 0)
            {
                var bsInputs = new NativeMethods.INPUT[backspaceCount * 2];
                for (int i = 0; i < backspaceCount; i++)
                {
                    bsInputs[i * 2] = MakeKeyInput(NativeMethods.VK_BACK, false);
                    bsInputs[i * 2 + 1] = MakeKeyInput(NativeMethods.VK_BACK, true);
                }
                NativeMethods.SendInput((uint)bsInputs.Length, bsInputs, Marshal.SizeOf<NativeMethods.INPUT>());

                // Chờ terminal xử lý backspace - 30ms đủ vì chạy trên background thread
                // (message pump không bị block như khi chạy trên hook callback)
                Thread.Sleep(30);
            }

            // 3. Gửi Ctrl+V
            var pasteInputs = new NativeMethods.INPUT[4];
            pasteInputs[0] = MakeKeyInput(NativeMethods.VK_CONTROL, false);
            pasteInputs[1] = MakeKeyInput(0x56, false);
            pasteInputs[2] = MakeKeyInput(0x56, true);
            pasteInputs[3] = MakeKeyInput(NativeMethods.VK_CONTROL, true);
            NativeMethods.SendInput(4, pasteInputs, Marshal.SizeOf<NativeMethods.INPUT>());

            DebugLog?.Invoke($"SendViaClipboard: bs={backspaceCount}, text='{text}'");

            // 4. Chờ paste xong trước khi xử lý phím tiếp theo
            Thread.Sleep(30);
        }
        finally
        {
            _isSendingInput = false;
        }
    }

    private static NativeMethods.INPUT MakeKeyInput(int vk, bool keyUp)
    {
        // Extended keys (arrow keys, Home, End, etc.) cần flag KEYEVENTF_EXTENDEDKEY
        // để hoạt động đúng, đặc biệt khi kết hợp với Shift
        bool isExtended = vk == (int)NativeMethods.VK_LEFT || vk == (int)NativeMethods.VK_RIGHT ||
                          vk == (int)NativeMethods.VK_UP || vk == (int)NativeMethods.VK_DOWN ||
                          vk == (int)NativeMethods.VK_HOME || vk == (int)NativeMethods.VK_END ||
                          vk == (int)NativeMethods.VK_PRIOR || vk == (int)NativeMethods.VK_NEXT ||
                          vk == (int)NativeMethods.VK_DELETE;

        uint flags = 0;
        if (keyUp) flags |= (uint)NativeMethods.KEYEVENTF_KEYUP;
        if (isExtended) flags |= (uint)NativeMethods.KEYEVENTF_EXTENDEDKEY;

        return new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = VIET_IME_MARKER
                }
            }
        };
    }

    private void SendBackspaces(int count)
    {
        if (count <= 0) return;
        _isSendingInput = true;
        try
        {
            var inputs = new NativeMethods.INPUT[count * 2];
            for (int i = 0; i < count; i++)
            {
                inputs[i * 2] = MakeKeyInput(NativeMethods.VK_BACK, false);
                inputs[i * 2 + 1] = MakeKeyInput(NativeMethods.VK_BACK, true);
            }
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }
        finally
        {
            _isSendingInput = false;
        }
    }

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
                inputs[i * 2] = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = 0, wScan = unicode,
                            dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                            time = 0, dwExtraInfo = VIET_IME_MARKER
                        }
                    }
                };
                inputs[i * 2 + 1] = new NativeMethods.INPUT
                {
                    type = NativeMethods.INPUT_KEYBOARD,
                    u = new NativeMethods.INPUTUNION
                    {
                        ki = new NativeMethods.KEYBDINPUT
                        {
                            wVk = 0, wScan = unicode,
                            dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP,
                            time = 0, dwExtraInfo = VIET_IME_MARKER
                        }
                    }
                };
            }
            NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        }
        finally
        {
            _isSendingInput = false;
        }
    }

    private bool SetClipboardText(string text)
    {
        for (int retry = 0; retry < 10; retry++)
        {
            if (NativeMethods.OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    NativeMethods.EmptyClipboard();
                    int byteCount = (text.Length + 1) * 2;
                    IntPtr hGlobal = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, (UIntPtr)byteCount);
                    if (hGlobal == IntPtr.Zero) return false;

                    IntPtr pGlobal = NativeMethods.GlobalLock(hGlobal);
                    if (pGlobal == IntPtr.Zero) { NativeMethods.GlobalFree(hGlobal); return false; }

                    Marshal.Copy(text.ToCharArray(), 0, pGlobal, text.Length);
                    Marshal.WriteInt16(pGlobal + text.Length * 2, 0);
                    NativeMethods.GlobalUnlock(hGlobal);

                    IntPtr result = NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, hGlobal);
                    if (result == IntPtr.Zero) { NativeMethods.GlobalFree(hGlobal); return false; }
                    return true;
                }
                finally { NativeMethods.CloseClipboard(); }
            }
            Thread.Sleep(3);
        }
        return false;
    }

    public void Dispose() { Dispose(true); GC.SuppressFinalize(this); }
    protected virtual void Dispose(bool disposing) { if (!_disposed) { Uninstall(); _disposed = true; } }
    ~KeyboardHook() { Dispose(false); }
}
