namespace VietIME.Core.Engines;

/// <summary>
/// Kết quả xử lý phím
/// </summary>
public class ProcessKeyResult
{
    /// <summary>
    /// Có xử lý phím không (true = đã xử lý, cần chặn phím gốc)
    /// </summary>
    public bool Handled { get; set; }
    
    /// <summary>
    /// Chuỗi ký tự cần gửi thay thế (nếu có)
    /// </summary>
    public string? OutputText { get; set; }
    
    /// <summary>
    /// Số ký tự cần xóa trước khi gửi OutputText
    /// </summary>
    public int BackspaceCount { get; set; }
    
    /// <summary>
    /// Buffer hiện tại sau khi xử lý
    /// </summary>
    public string CurrentBuffer { get; set; } = string.Empty;
}

/// <summary>
/// Interface cho các engine xử lý input tiếng Việt
/// </summary>
public interface IInputEngine
{
    /// <summary>
    /// Tên engine (Telex, VNI, etc.)
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// Xử lý phím được nhấn
    /// </summary>
    /// <param name="key">Ký tự được nhấn</param>
    /// <param name="isShiftPressed">Phím Shift có được nhấn không</param>
    /// <returns>Kết quả xử lý</returns>
    ProcessKeyResult ProcessKey(char key, bool isShiftPressed);
    
    /// <summary>
    /// Reset buffer (khi chuyển ứng dụng, nhấn Space, Enter, etc.)
    /// </summary>
    void Reset();
    
    /// <summary>
    /// Xử lý phím Backspace
    /// </summary>
    /// <returns>true nếu đã xử lý trong buffer, false nếu cần truyền xuống</returns>
    bool ProcessBackspace();
    
    /// <summary>
    /// Lấy buffer hiện tại
    /// </summary>
    string GetBuffer();
}
