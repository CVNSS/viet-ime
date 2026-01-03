# VietIME - Bộ gõ tiếng Việt cho Windows

![Version](https://img.shields.io/badge/version-1.0.0-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-green.svg)
![Framework](https://img.shields.io/badge/.NET-8.0-purple.svg)

Bộ gõ tiếng Việt **thay thế Unikey**, hoạt động **system-wide** trên Windows 10/11.

## ✨ Tính năng

- ✅ Hoạt động trên **tất cả ứng dụng Windows**
  - Microsoft Office (Word, Excel, PowerPoint, Outlook)
  - Trình duyệt web (Chrome, Edge, Firefox, Brave)
  - IDE/Editor (VS Code, Visual Studio, JetBrains)
  - Terminal/PowerShell
  - Notepad, Explorer, và mọi ứng dụng Win32/UWP
- ✅ Hỗ trợ **Telex** và **VNI**
- ✅ **System tray** với menu context
- ✅ **Hotkey** bật/tắt nhanh (Ctrl + Shift)
- ✅ Giao diện cài đặt đơn giản

## 🚀 Tải và Sử dụng

### Tải xuống (Portable - Không cần cài đặt)

1. Tải file `VietIME.exe` từ [Releases](../../releases)
2. Chạy trực tiếp - **Không cần cài đặt .NET!**
3. Icon xuất hiện trên system tray

> ⚠️ **Lưu ý**: Windows SmartScreen có thể cảnh báo vì app chưa có chữ ký số. Click "More info" → "Run anyway".

### Yêu cầu hệ thống
- Windows 10/11 (64-bit)
- Không cần cài đặt thêm gì!

### Build từ source

```powershell
# Clone repo
git clone https://github.com/YOUR_USERNAME/viet-ime.git
cd viet-ime

# Build
dotnet build

# Chạy
dotnet run --project src/VietIME.App
```

### Build Portable EXE

```powershell
dotnet publish src/VietIME.App -c Release -o ./publish
# Kết quả: publish/VietIME.exe (~68MB, self-contained)
```

## 📖 Cách sử dụng

### Telex (mặc định)
| Gõ | Kết quả | Mô tả |
|----|---------|-------|
| `aa` | â | a mũ |
| `aw` | ă | a móc |
| `ee` | ê | e mũ |
| `oo` | ô | o mũ |
| `ow` hoặc `]` | ơ | o móc |
| `uw` hoặc `[` | ư | u móc |
| `dd` | đ | đ |
| `s` | sắc | dấu sắc |
| `f` | huyền | dấu huyền |
| `r` | hỏi | dấu hỏi |
| `x` | ngã | dấu ngã |
| `j` | nặng | dấu nặng |

**Ví dụ:** `viet65nam` → `việtnam` (Telex: `vietnams`)

### VNI
| Gõ | Kết quả | Mô tả |
|----|---------|-------|
| `6` | â/ê/ô | mũ |
| `7` | ă/ư | móc |
| `8` | ơ | o móc |
| `9` | đ | đ |
| `1` | sắc | dấu sắc |
| `2` | huyền | dấu huyền |
| `3` | hỏi | dấu hỏi |
| `4` | ngã | dấu ngã |
| `5` | nặng | dấu nặng |

**Ví dụ:** `vie65tnam` → `việtnam` (VNI: `viet61nam`)

## ⌨️ Phím tắt

| Phím tắt | Chức năng |
|----------|-----------|
| `Ctrl + Shift` | Bật/tắt VietIME |
| Double-click tray icon | Bật/tắt VietIME |
| Right-click tray icon | Menu context |

## 🏗️ Cấu trúc dự án

```
viet-ime/
├── src/
│   ├── VietIME.Core/         # Engine xử lý tiếng Việt
│   │   ├── Engines/          # Telex, VNI engines
│   │   ├── Models/           # Bảng mã ký tự
│   │   └── Utils/
│   ├── VietIME.Hook/         # Keyboard hook (Win32 API)
│   │   ├── KeyboardHook.cs   # Hook manager
│   │   └── NativeMethods.cs  # P/Invoke declarations
│   └── VietIME.App/          # WPF Application
│       ├── App.xaml          # Entry point + tray icon
│       └── MainWindow.xaml   # Settings UI
├── tests/
├── docs/
└── README.md
```

## ⚠️ Lưu ý

1. **Antivirus**: Keyboard hook có thể bị một số phần mềm antivirus cảnh báo. Đây là hành vi bình thường cho các bộ gõ.

2. **Quyền Admin**: Một số ứng dụng chạy với quyền admin có thể không nhận được input từ VietIME nếu VietIME không chạy với quyền admin.

3. **Conflict**: Nên tắt các bộ gõ khác (Unikey, EVKey) trước khi sử dụng VietIME để tránh xung đột.

## 🔧 Phát triển

### Thêm engine mới

1. Tạo class mới implement `IInputEngine`
2. Override các method: `ProcessKey`, `Reset`, `ProcessBackspace`
3. Đăng ký engine trong `App.xaml.cs`

### API chính

```csharp
// Engine interface
public interface IInputEngine
{
    string Name { get; }
    ProcessKeyResult ProcessKey(char key, bool isShiftPressed);
    void Reset();
    bool ProcessBackspace();
}

// Keyboard hook
var hook = new KeyboardHook();
hook.Engine = new TelexEngine();
hook.Install();
```

## 📝 License

MIT License - Sử dụng tự do cho mục đích cá nhân và thương mại.

## 🤝 Đóng góp

Pull requests are welcome! Để đóng góp:

1. Fork repo
2. Tạo branch (`git checkout -b feature/AmazingFeature`)
3. Commit changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to branch (`git push origin feature/AmazingFeature`)
5. Mở Pull Request

---

**VietIME** - Made with ❤️ for Vietnamese users
