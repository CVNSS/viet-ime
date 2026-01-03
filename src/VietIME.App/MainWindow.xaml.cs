using System.Windows;
using VietIME.Core.Engines;
using VietIME.Hook;

namespace VietIME.App;

/// <summary>
/// Cửa sổ cài đặt VietIME
/// </summary>
public partial class MainWindow : Window
{
    private readonly KeyboardHook? _hook;
    
    public MainWindow(KeyboardHook? hook = null)
    {
        InitializeComponent();
        _hook = hook;
        
        // Sync UI với trạng thái hiện tại
        if (_hook != null)
        {
            chkEnabled.IsChecked = _hook.IsEnabled;
            rbTelex.IsChecked = _hook.Engine?.Name == "Telex";
            rbVNI.IsChecked = _hook.Engine?.Name == "VNI";
            UpdateStatus();
            
            _hook.EnabledChanged += (s, e) => Dispatcher.Invoke(UpdateStatus);
            
            // Subscribe to debug log
            _hook.DebugLog += msg => Dispatcher.Invoke(() => 
            {
                txtDebug.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
                txtDebug.ScrollToEnd();
            });
        }
        
        Log("MainWindow initialized");
        Log($"Hook installed: {_hook != null}");
    }
    
    private void Log(string msg)
    {
        txtDebug.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n");
        txtDebug.ScrollToEnd();
    }
    
    private void UpdateStatus()
    {
        if (_hook == null) return;
        
        var enabled = _hook.IsEnabled;
        var engineName = _hook.Engine?.Name ?? "Telex";
        
        txtStatus.Text = enabled 
            ? $"Đang bật - {engineName}" 
            : "Đã tắt";
        txtStatus.Foreground = enabled 
            ? System.Windows.Media.Brushes.Green 
            : System.Windows.Media.Brushes.Gray;
        
        chkEnabled.IsChecked = enabled;
    }
    
    private void ChkEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (_hook != null)
        {
            _hook.IsEnabled = chkEnabled.IsChecked ?? false;
        }
    }
    
    private void InputMethod_Changed(object sender, RoutedEventArgs e)
    {
        if (_hook == null) return;
        
        if (rbTelex.IsChecked == true)
        {
            _hook.Engine = new TelexEngine();
        }
        else if (rbVNI.IsChecked == true)
        {
            _hook.Engine = new VniEngine();
        }
        
        UpdateStatus();
    }
    
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
    
    private void BtnExit_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
}
