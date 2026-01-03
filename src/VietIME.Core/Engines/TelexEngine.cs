using VietIME.Core.Models;

namespace VietIME.Core.Engines;

/// <summary>
/// Engine xử lý kiểu gõ Telex
/// Quy tắc Telex:
/// - Dấu: s=sắc, f=huyền, r=hỏi, x=ngã, j=nặng
/// - Mũ: aa=â, ee=ê, oo=ô, aw=ă, ow=ơ, uw=ư
/// - Đặc biệt: dd=đ, w sau u/o = ư/ơ
/// </summary>
public class TelexEngine : IInputEngine
{
    public string Name => "Telex";
    
    // Buffer lưu từ đang gõ
    private readonly List<char> _buffer = [];
    
    // Map phím dấu thanh Telex
    private static readonly Dictionary<char, VietnameseChar.ToneIndex> ToneKeys = new()
    {
        ['s'] = VietnameseChar.ToneIndex.Acute,  // Sắc
        ['f'] = VietnameseChar.ToneIndex.Grave,  // Huyền
        ['r'] = VietnameseChar.ToneIndex.Hook,   // Hỏi
        ['x'] = VietnameseChar.ToneIndex.Tilde,  // Ngã
        ['j'] = VietnameseChar.ToneIndex.Dot,    // Nặng
        ['z'] = VietnameseChar.ToneIndex.None,   // Xóa dấu
    };
    
    public ProcessKeyResult ProcessKey(char key, bool isShiftPressed)
    {
        var result = new ProcessKeyResult();
        char lowerKey = char.ToLower(key);
        
        // Xử lý phím tắt '[' = ư, ']' = ơ (TRƯỚC khi kiểm tra IsLetter)
        if (key == '[' || key == ']')
        {
            var bracketResult = TryProcessBracket(key);
            if (bracketResult.HasValue)
            {
                result.Handled = true;
                result.BackspaceCount = bracketResult.Value.backspaceCount;
                result.OutputText = bracketResult.Value.output;
                result.CurrentBuffer = GetBuffer();
                return result;
            }
        }
        
        // Nếu là ký tự không phải chữ cái -> reset buffer
        if (!char.IsLetter(key))
        {
            Reset();
            result.Handled = false;
            return result;
        }
        
        // Xử lý phím dấu thanh (s, f, r, x, j, z)
        if (ToneKeys.TryGetValue(lowerKey, out var toneIndex))
        {
            var toneResult = TryApplyTone(toneIndex, key);
            if (toneResult.HasValue)
            {
                result.Handled = true;
                result.BackspaceCount = toneResult.Value.backspaceCount;
                result.OutputText = toneResult.Value.output;
                result.CurrentBuffer = GetBuffer();
                return result;
            }
        }
        
        // Xử lý 'd' -> 'đ'
        if (lowerKey == 'd')
        {
            var dResult = TryProcessD(key);
            if (dResult.HasValue)
            {
                result.Handled = true;
                result.BackspaceCount = dResult.Value.backspaceCount;
                result.OutputText = dResult.Value.output;
                result.CurrentBuffer = GetBuffer();
                return result;
            }
        }
        
        // Xử lý 'w' -> ă, ơ, ư
        if (lowerKey == 'w')
        {
            var wResult = TryProcessW(key);
            if (wResult.HasValue)
            {
                result.Handled = true;
                result.BackspaceCount = wResult.Value.backspaceCount;
                result.OutputText = wResult.Value.output;
                result.CurrentBuffer = GetBuffer();
                return result;
            }
        }
        
        // Xử lý nguyên âm đôi (aa, ee, oo)
        if (lowerKey is 'a' or 'e' or 'o')
        {
            var doubleResult = TryProcessDoubleVowel(key);
            if (doubleResult.HasValue)
            {
                result.Handled = true;
                result.BackspaceCount = doubleResult.Value.backspaceCount;
                result.OutputText = doubleResult.Value.output;
                result.CurrentBuffer = GetBuffer();
                return result;
            }
        }
        
        // Không xử lý đặc biệt -> thêm vào buffer
        _buffer.Add(key);
        result.Handled = false;
        result.CurrentBuffer = GetBuffer();
        return result;
    }
    
    /// <summary>
    /// Tìm vị trí nguyên âm để đặt dấu (theo quy tắc tiếng Việt)
    /// </summary>
    private int FindVowelPositionForTone()
    {
        // Quy tắc đặt dấu tiếng Việt:
        // 1. Nếu có nguyên âm mũ/móc (ê, ô, ơ, â, ă, ư) -> đặt dấu vào đó
        // 2. Với nguyên âm đôi bắt đầu bằng i/u (ie, ia, ua, uo, ưa, ươ) -> đặt dấu vào nguyên âm sau
        // 3. Với oa, oe, uy -> đặt dấu vào nguyên âm sau
        // 4. Trường hợp khác: nếu kết thúc bằng phụ âm -> nguyên âm đầu, ngược lại -> nguyên âm sau
        
        var vowelPositions = new List<int>();
        
        for (int i = 0; i < _buffer.Count; i++)
        {
            if (IsVowel(_buffer[i]))
            {
                vowelPositions.Add(i);
            }
        }
        
        if (vowelPositions.Count == 0)
            return -1;
        
        if (vowelPositions.Count == 1)
            return vowelPositions[0];
        
        // Tìm nhóm nguyên âm liền nhau cuối cùng
        var lastGroup = new List<int>();
        for (int i = vowelPositions.Count - 1; i >= 0; i--)
        {
            if (lastGroup.Count == 0 || vowelPositions[i] == lastGroup[0] - 1)
            {
                lastGroup.Insert(0, vowelPositions[i]);
            }
            else
            {
                break;
            }
        }
        
        if (lastGroup.Count == 1)
            return lastGroup[0];
        
        if (lastGroup.Count >= 2)
        {
            char firstVowel = char.ToLower(VietnameseChar.GetVowelWithoutTone(_buffer[lastGroup[0]]));
            char secondVowel = char.ToLower(VietnameseChar.GetVowelWithoutTone(_buffer[lastGroup[1]]));
            
            // ƯƠ -> dấu vào Ơ (nguyên âm SAU) khi có phụ âm sau
            // Ví dụ: được, mướn, dường
            // PHẢI kiểm tra TRƯỚC khi tìm nguyên âm có mũ/móc
            if (firstVowel == 'ư' && secondVowel == 'ơ')
            {
                int lastVowelPos = lastGroup[^1];
                bool hasConsonantAfter = lastVowelPos < _buffer.Count - 1 && 
                                         !IsVowel(_buffer[lastVowelPos + 1]);
                // Nếu có phụ âm sau -> dấu vào ơ
                if (hasConsonantAfter)
                {
                    return lastGroup[1];
                }
            }
            
            // oa, oe, oă -> dấu vào nguyên âm SAU (a, e, ă)
            // Ví dụ: hoà, toé, loạn
            if (firstVowel == 'o' && secondVowel is 'a' or 'e' or 'ă')
            {
                return lastGroup[1];
            }
        }
        
        // Ưu tiên nguyên âm có mũ/móc (ê, ô, ơ, â, ă, ư)
        foreach (int pos in lastGroup)
        {
            char c = _buffer[pos];
            // Lấy nguyên âm không dấu thanh (giữ mũ/móc)
            char vowelWithoutTone = char.ToLower(VietnameseChar.GetVowelWithoutTone(c));
            if (vowelWithoutTone is 'ê' or 'ô' or 'ơ' or 'â' or 'ă' or 'ư')
            {
                return pos;
            }
        }
        
        if (lastGroup.Count >= 2)
        {
            // TẤT CẢ các trường hợp khác -> dấu vào nguyên âm ĐẦU
            // Ví dụ: lại (ai), mùa (ua), mía (ia), chạy (ay), hỏi (ơi)
            return lastGroup[0];
        }
        
        // 3 nguyên âm -> giữa
        return lastGroup[1];
    }
    
    private bool IsVowel(char c)
    {
        char lower = char.ToLower(c);
        return lower is 'a' or 'ă' or 'â' or 'e' or 'ê' or 'i' or 'o' or 'ô' or 'ơ' or 'u' or 'ư' or 'y'
               || VietnameseChar.IsVietnameseVowel(c);
    }
    
    private (int backspaceCount, string output)? TryApplyTone(VietnameseChar.ToneIndex tone, char originalKey)
    {
        // Áp dụng các quy tắc thông minh trước khi đặt dấu
        AutoCorrectDPattern();      // d + vowel + d -> đ + vowel
        AutoConvertDWithUO();       // d + uo/ưo -> đ + uo/ưo
        AutoConvertUoToUoHorn();    // ưo + consonant -> ươ
        
        int vowelPos = FindVowelPositionForTone();
        
        if (vowelPos < 0)
        {
            // Không có nguyên âm -> thêm key vào buffer như bình thường
            return null;
        }
        
        char oldVowel = _buffer[vowelPos];
        var currentTone = VietnameseChar.GetToneIndex(oldVowel);
        
        // Toggle: Nếu nguyên âm đã có dấu giống dấu đang gõ -> xoá dấu và thêm ký tự gốc
        // Ví dụ: tés + s -> tess (xoá dấu sắc, thêm 's')
        if (currentTone == tone && tone != VietnameseChar.ToneIndex.None)
        {
            // Xoá dấu thanh
            char vowelWithoutTone = VietnameseChar.ApplyTone(oldVowel, VietnameseChar.ToneIndex.None);
            _buffer[vowelPos] = vowelWithoutTone;
            
            // Thêm ký tự dấu gốc vào buffer
            _buffer.Add(originalKey);
            
            int toggleBackspace = _buffer.Count - vowelPos - 1; // -1 vì ký tự mới thêm không cần backspace
            string toggleOutput = new string(_buffer.Skip(vowelPos).ToArray());
            
            return (toggleBackspace, toggleOutput);
        }
        
        char newVowel = VietnameseChar.ApplyTone(oldVowel, tone);
        
        if (newVowel == oldVowel)
        {
            // Không thay đổi -> trả về key gốc
            return null;
        }
        
        // Cập nhật buffer
        _buffer[vowelPos] = newVowel;
        
        // Tính số backspace và output
        int backspaceCount = _buffer.Count - vowelPos;
        string output = new string(_buffer.Skip(vowelPos).ToArray());
        
        return (backspaceCount, output);
    }
    
    /// <summary>
    /// Quy tắc thông minh: Tự động chuyển pattern 'd' + nguyên âm + 'd' thành 'đ' + nguyên âm
    /// Ví dụ: "dud" -> "đu", "did" -> "đi", "duod" -> "đuo"
    /// Điều này cho phép gõ linh hoạt hơn khi không gõ 'dd' liền nhau
    /// </summary>
    private void AutoCorrectDPattern()
    {
        // Tìm pattern: d + nguyên âm(s) + d
        // Chỉ xử lý khi 'd' đầu là chữ thường (chưa phải 'đ')
        if (_buffer.Count < 3)
            return;
        
        // Kiểm tra 'd' đầu tiên
        char first = _buffer[0];
        if (char.ToLower(first) != 'd')
            return;
        
        // Nếu đã là 'đ' rồi thì bỏ qua
        if (first == 'đ' || first == 'Đ')
            return;
        
        // Tìm 'd' thứ hai sau các nguyên âm
        int secondDPos = -1;
        bool hasVowelBetween = false;
        
        for (int i = 1; i < _buffer.Count; i++)
        {
            char c = _buffer[i];
            if (IsVowel(c))
            {
                hasVowelBetween = true;
            }
            else if (char.ToLower(c) == 'd' && hasVowelBetween)
            {
                secondDPos = i;
                break;
            }
        }
        
        if (secondDPos > 0 && hasVowelBetween)
        {
            // Chuyển 'd' đầu thành 'đ'
            bool isUpper = char.IsUpper(first);
            _buffer[0] = isUpper ? 'Đ' : 'đ';
            
            // Xóa 'd' thứ hai
            _buffer.RemoveAt(secondDPos);
        }
    }
    
    /// <summary>
    /// Quy tắc thông minh: Tự động chuyển 'd' đầu từ thành 'đ' khi có pattern 'ươ' hoặc 'uo'
    /// Ví dụ: "duo" + w -> "đươ" (thay vì "duơ")
    /// </summary>
    private void AutoConvertDWithUO()
    {
        if (_buffer.Count < 3)
            return;
        
        char first = _buffer[0];
        // Chỉ xử lý nếu bắt đầu bằng 'd' thường (chưa phải 'đ')
        if (char.ToLower(first) != 'd' || first == 'đ' || first == 'Đ')
            return;
        
        // Kiểm tra có pattern 'u' + 'o' hoặc 'ư' + 'o' hoặc 'ư' + 'ơ' sau 'd' không
        for (int i = 1; i < _buffer.Count - 1; i++)
        {
            char c1 = char.ToLower(VietnameseChar.GetVowelWithoutTone(_buffer[i]));
            char c2 = char.ToLower(VietnameseChar.GetVowelWithoutTone(_buffer[i + 1]));
            
            if ((c1 == 'u' || c1 == 'ư') && (c2 == 'o' || c2 == 'ơ'))
            {
                // Có pattern uo/ưo/ươ -> chuyển 'd' thành 'đ'
                bool isUpper = char.IsUpper(first);
                _buffer[0] = isUpper ? 'Đ' : 'đ';
                return;
            }
        }
    }
    
    /// <summary>
    /// Tự động chuyển 'ưo' + phụ âm thành 'ươ'
    /// Trong tiếng Việt, 'ưo' + phụ âm + dấu không tồn tại, phải là 'ươ'
    /// Ví dụ: đưoc + j -> được (tự động chuyển o -> ơ)
    /// </summary>
    private void AutoConvertUoToUoHorn()
    {
        for (int i = 0; i < _buffer.Count - 1; i++)
        {
            char current = char.ToLower(VietnameseChar.GetVowelWithoutTone(_buffer[i]));
            char next = char.ToLower(VietnameseChar.GetVowelWithoutTone(_buffer[i + 1]));
            
            // Tìm pattern: ư + o
            if (current == 'ư' && next == 'o')
            {
                // Kiểm tra có phụ âm sau không
                bool hasConsonantAfter = false;
                for (int j = i + 2; j < _buffer.Count; j++)
                {
                    if (!IsVowel(_buffer[j]))
                    {
                        hasConsonantAfter = true;
                        break;
                    }
                }
                
                if (hasConsonantAfter)
                {
                    // Chuyển 'o' thành 'ơ', giữ nguyên hoa/thường và dấu (nếu có)
                    char oChar = _buffer[i + 1];
                    bool isUpper = char.IsUpper(oChar);
                    var existingTone = VietnameseChar.GetToneIndex(oChar);
                    
                    char newO = isUpper ? 'Ơ' : 'ơ';
                    if (existingTone != VietnameseChar.ToneIndex.None)
                    {
                        newO = VietnameseChar.ApplyTone(newO, existingTone);
                    }
                    
                    _buffer[i + 1] = newO;
                }
            }
        }
    }
    
    private (int backspaceCount, string output)? TryProcessD(char key)
    {
        if (_buffer.Count == 0)
            return null;
        
        char lastChar = _buffer[^1];
        char lowerLast = char.ToLower(lastChar);
        
        // dd -> đ
        if (lowerLast == 'd')
        {
            bool isUpper = char.IsUpper(lastChar) || char.IsUpper(key);
            char newChar = isUpper ? VietnameseChar.UpperD : VietnameseChar.LowerD;
            
            _buffer[^1] = newChar;
            
            return (1, newChar.ToString());
        }
        
        return null;
    }
    
    /// <summary>
    /// Xử lý phím tắt '[' = ư, ']' = ơ
    /// </summary>
    private (int backspaceCount, string output)? TryProcessBracket(char key)
    {
        if (key == '[')
        {
            _buffer.Add('ư');
            return (0, "ư");
        }
        if (key == ']')
        {
            _buffer.Add('ơ');
            return (0, "ơ");
        }
        return null;
    }
    
    private (int backspaceCount, string output)? TryProcessW(char key)
    {
        // Nếu buffer rỗng -> không xử lý, để ra chữ 'w' thường
        if (_buffer.Count == 0)
        {
            return null;
        }
        
        // Kiểm tra ký tự cuối - nếu đã là ư/ơ/ă thì toggle ngược lại
        char lastChar = _buffer[^1];
        char lastLower = char.ToLower(VietnameseChar.GetVowelWithoutTone(lastChar));
        
        // Toggle: ư -> u, ơ -> o, ă -> a (gõ w lần 2 để hủy)
        if (lastLower is 'ư' or 'ơ' or 'ă')
        {
            bool isUpper = char.IsUpper(lastChar);
            var existingTone = VietnameseChar.GetToneIndex(lastChar);
            
            char originalVowel = lastLower switch
            {
                'ư' => isUpper ? 'U' : 'u',
                'ơ' => isUpper ? 'O' : 'o',
                'ă' => isUpper ? 'A' : 'a',
                _ => lastChar
            };
            
            // Áp dụng lại dấu thanh nếu có
            if (existingTone != VietnameseChar.ToneIndex.None)
            {
                originalVowel = VietnameseChar.ApplyTone(originalVowel, existingTone);
            }
            
            _buffer[^1] = originalVowel;
            
            // Thêm 'w' vào buffer
            _buffer.Add('w');
            
            return (1, originalVowel.ToString() + "w");
        }
        
        // Kiểm tra pattern đặc biệt: 'uo' hoặc 'ưo' -> chuyển thành 'ươ'
        // Ví dụ: "duo" + w -> "dươ", "dưo" + w -> "dươ"
        if (_buffer.Count >= 2)
        {
            char secondLast = _buffer[^2];
            char secondLastLower = char.ToLower(VietnameseChar.GetVowelWithoutTone(secondLast));
            
            // Pattern: u + o -> ư + ơ
            if ((secondLastLower == 'u' || secondLastLower == 'ư') && lastLower == 'o')
            {
                // Chuyển 'u' thành 'ư' (nếu chưa)
                if (secondLastLower == 'u')
                {
                    bool isUpper = char.IsUpper(secondLast);
                    var tone = VietnameseChar.GetToneIndex(secondLast);
                    char newU = isUpper ? 'Ư' : 'ư';
                    if (tone != VietnameseChar.ToneIndex.None)
                    {
                        newU = VietnameseChar.ApplyTone(newU, tone);
                    }
                    _buffer[^2] = newU;
                }
                
                // Chuyển 'o' thành 'ơ'
                {
                    bool isUpper = char.IsUpper(lastChar);
                    var tone = VietnameseChar.GetToneIndex(lastChar);
                    char newO = isUpper ? 'Ơ' : 'ơ';
                    if (tone != VietnameseChar.ToneIndex.None)
                    {
                        newO = VietnameseChar.ApplyTone(newO, tone);
                    }
                    _buffer[^1] = newO;
                }
                
                // Chỉ trả về 2 ký tự cuối (ươ) - không thay đổi d -> đ ở đây
                // Quy tắc d -> đ sẽ được xử lý khi thêm dấu thanh
                int backspaceCount = 2;
                string output = new string(_buffer.Skip(_buffer.Count - 2).ToArray());
                return (backspaceCount, output);
            }
        }
        
        // Tìm nguyên âm gần nhất có thể chuyển đổi (không dừng khi gặp phụ âm)
        // Để hỗ trợ gõ linh hoạt hơn, ví dụ: "dudw" -> "dưd" thay vì "dudw"
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            char c = _buffer[i];
            char lowerC = char.ToLower(c);
            
            // Chỉ xử lý a, o, u (ă, ô đã được transform rồi)
            if (lowerC is 'a' or 'o' or 'u')
            {
                char newVowel = VietnameseChar.TransformVowel(c, 'w');
                
                if (newVowel != c)
                {
                    _buffer[i] = newVowel;
                    
                    int backspaceCount = _buffer.Count - i;
                    string output = new string(_buffer.Skip(i).ToArray());
                    
                    return (backspaceCount, output);
                }
            }
            // Tiếp tục tìm, không break khi gặp phụ âm
        }
        
        // Không tìm thấy nguyên âm để chuyển -> không xử lý, để ra chữ 'w' thường
        return null;
    }
    
    private (int backspaceCount, string output)? TryProcessDoubleVowel(char key)
    {
        if (_buffer.Count == 0)
            return null;
        
        char lastChar = _buffer[^1];
        char lowerLast = char.ToLower(lastChar);
        char lowerKey = char.ToLower(key);
        
        // aa -> â, ee -> ê, oo -> ô
        if (lowerLast == lowerKey)
        {
            char newVowel = lowerKey switch
            {
                'a' => 'â',
                'e' => 'ê',
                'o' => 'ô',
                _ => lastChar
            };
            
            if (char.IsUpper(lastChar))
            {
                newVowel = char.ToUpper(newVowel);
            }
            
            // Giữ lại dấu thanh nếu có
            var currentTone = VietnameseChar.GetToneIndex(lastChar);
            if (currentTone != VietnameseChar.ToneIndex.None)
            {
                newVowel = VietnameseChar.ApplyTone(newVowel, currentTone);
            }
            
            _buffer[^1] = newVowel;
            
            return (1, newVowel.ToString());
        }
        
        return null;
    }
    
    public void Reset()
    {
        _buffer.Clear();
    }
    
    public bool ProcessBackspace()
    {
        if (_buffer.Count > 0)
        {
            _buffer.RemoveAt(_buffer.Count - 1);
            return false; // Vẫn cần gửi backspace xuống
        }
        return false;
    }
    
    public string GetBuffer()
    {
        return new string(_buffer.ToArray());
    }
}
