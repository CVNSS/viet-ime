# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Dự án

VietIME - Bộ gõ tiếng Việt cho Windows, thay thế Unikey. Hoạt động system-wide trên Windows 10/11 sử dụng .NET 8.

**Điểm nhấn**: Hỗ trợ gõ tiếng Việt trên **Terminal, PowerShell, và các IDE** như Warp, VS Code Terminal - nơi mà Unikey/EVKey thường gặp vấn đề.

## Build & Run Commands

```powershell
# Build toàn bộ solution
dotnet build

# Chạy ứng dụng
dotnet run --project src/VietIME.App

# Build portable EXE (self-contained, single file)
dotnet publish src/VietIME.App -c Release -o ./publish
```

## Kiến trúc

```
src/
├── VietIME.Core/        # Engine xử lý tiếng Việt (không phụ thuộc Windows)
│   ├── Engines/         # IInputEngine implementations
│   │   ├── IInputEngine.cs    # Interface định nghĩa engine
│   │   ├── TelexEngine.cs     # Kiểu gõ Telex
│   │   └── VniEngine.cs       # Kiểu gõ VNI
│   └── Models/
│       └── VietnameseChar.cs  # Bảng mã Unicode tiếng Việt, utility functions
│
├── VietIME.Hook/        # Keyboard hook layer (Win32 API)
│   ├── KeyboardHook.cs  # Low-level keyboard hook, SendInput/Clipboard
│   └── NativeMethods.cs # P/Invoke declarations
│
└── VietIME.App/         # WPF Application
    └── App.xaml.cs      # System tray, engine management
```

### Luồng xử lý phím

1. `KeyboardHook.HookCallback()` bắt mọi keypress qua `SetWindowsHookEx(WH_KEYBOARD_LL)`
2. Chuyển virtual key thành char qua `NativeMethods.VirtualKeyToChar()`
3. Gọi `IInputEngine.ProcessKey()` → trả về `ProcessKeyResult`
4. Nếu `Handled=true`: gửi backspaces + output text qua Clipboard (Ctrl+V) hoặc SendInput

### Cơ chế gửi text

- **Clipboard method** (mặc định): Backspaces + SetClipboard + Ctrl+V. Hoạt động với Terminal/PowerShell
- **SendInput method**: KEYEVENTF_UNICODE. Nhanh hơn nhưng không hỗ trợ terminal

## Thêm Engine mới

1. Tạo class implement `IInputEngine` trong `VietIME.Core/Engines/`:

```csharp
public class MyEngine : IInputEngine
{
    public string Name => "MyEngine";
    
    public ProcessKeyResult ProcessKey(char key, bool isShiftPressed)
    {
        // Xử lý phím, trả về:
        // - Handled: true nếu cần chặn phím gốc
        // - BackspaceCount: số ký tự cần xóa
        // - OutputText: chuỗi thay thế
    }
    
    public void Reset() { /* Reset buffer khi chuyển từ/app */ }
    public bool ProcessBackspace() { /* Xử lý Backspace */ }
    public string GetBuffer() { /* Lấy buffer hiện tại */ }
}
```

2. Đăng ký trong `App.xaml.cs` method `SetEngine()`

## Quy tắc đặt dấu tiếng Việt

Logic trong `TelexEngine.FindVowelPositionForTone()`:
- Nguyên âm có mũ/móc (ê, ô, ơ, â, ă, ư) được ưu tiên
- Pattern đặc biệt: `ươ` → dấu vào `ơ` khi có phụ âm sau
- `oa`, `oe`, `oă` → dấu vào nguyên âm sau
- Mặc định: nguyên âm đầu trong nhóm nguyên âm liền nhau

## Test thủ công

Chạy file `TestHook.cs` để test keyboard hook:
```powershell
dotnet run TestHook.cs
```

## Lưu ý kỹ thuật

- Delegate `LowLevelKeyboardProc` phải được giữ reference (field) để tránh GC
- Extra info marker `0x56494D45` ("VIME") để nhận diện input từ chính VietIME
- Buffer timeout 2 giây - tự động reset nếu không gõ
- Hotkey toggle: Ctrl + Shift
