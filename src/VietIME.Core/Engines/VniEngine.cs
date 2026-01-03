using VietIME.Core.Models;

namespace VietIME.Core.Engines;

/// <summary>
/// Engine xử lý kiểu gõ VNI
/// Quy tắc VNI:
/// - Dấu: 1=sắc, 2=huyền, 3=hỏi, 4=ngã, 5=nặng
/// - Mũ: 6=â/ê/ô, 7=ă/ư, 8=ơ, 9=đ
/// </summary>
public class VniEngine : IInputEngine
{
    public string Name => "VNI";
    
    // Buffer lưu từ đang gõ
    private readonly List<char> _buffer = [];
    
    // Map phím số -> dấu thanh VNI
    private static readonly Dictionary<char, VietnameseChar.ToneIndex> ToneKeys = new()
    {
        ['1'] = VietnameseChar.ToneIndex.Acute,  // Sắc
        ['2'] = VietnameseChar.ToneIndex.Grave,  // Huyền
        ['3'] = VietnameseChar.ToneIndex.Hook,   // Hỏi
        ['4'] = VietnameseChar.ToneIndex.Tilde,  // Ngã
        ['5'] = VietnameseChar.ToneIndex.Dot,    // Nặng
        ['0'] = VietnameseChar.ToneIndex.None,   // Xóa dấu
    };
    
    public ProcessKeyResult ProcessKey(char key, bool isShiftPressed)
    {
        var result = new ProcessKeyResult();
        
        // Xử lý số cho dấu thanh (1-5) và mũ (6-9)
        if (char.IsDigit(key))
        {
            // Dấu thanh (1-5, 0)
            if (ToneKeys.TryGetValue(key, out var toneIndex))
            {
                var toneResult = TryApplyTone(toneIndex);
                if (toneResult.HasValue)
                {
                    result.Handled = true;
                    result.BackspaceCount = toneResult.Value.backspaceCount;
                    result.OutputText = toneResult.Value.output;
                    result.CurrentBuffer = GetBuffer();
                    return result;
                }
            }
            
            // Mũ (6-9)
            var hatResult = TryApplyHat(key);
            if (hatResult.HasValue)
            {
                result.Handled = true;
                result.BackspaceCount = hatResult.Value.backspaceCount;
                result.OutputText = hatResult.Value.output;
                result.CurrentBuffer = GetBuffer();
                return result;
            }
            
            // Không xử lý được -> reset buffer (đang gõ số)
            Reset();
            result.Handled = false;
            return result;
        }
        
        // Nếu là ký tự không phải chữ cái -> reset buffer
        if (!char.IsLetter(key))
        {
            Reset();
            result.Handled = false;
            return result;
        }
        
        // Thêm vào buffer
        _buffer.Add(key);
        result.Handled = false;
        result.CurrentBuffer = GetBuffer();
        return result;
    }
    
    private (int backspaceCount, string output)? TryApplyTone(VietnameseChar.ToneIndex tone)
    {
        int vowelPos = FindVowelPositionForTone();
        
        if (vowelPos < 0)
            return null;
        
        char oldVowel = _buffer[vowelPos];
        char newVowel = VietnameseChar.ApplyTone(oldVowel, tone);
        
        if (newVowel == oldVowel)
            return null;
        
        _buffer[vowelPos] = newVowel;
        
        int backspaceCount = _buffer.Count - vowelPos;
        string output = new string(_buffer.Skip(vowelPos).ToArray());
        
        return (backspaceCount, output);
    }
    
    private (int backspaceCount, string output)? TryApplyHat(char key)
    {
        if (_buffer.Count == 0)
            return null;
        
        // 6: â/ê/ô, 7: ă/ư, 8: ơ, 9: đ
        switch (key)
        {
            case '6':
                return TryApplyHat6();
            case '7':
                return TryApplyHat7();
            case '8':
                return TryApplyHat8();
            case '9':
                return TryApplyHat9();
            default:
                return null;
        }
    }
    
    // 6: a->â, e->ê, o->ô
    private (int backspaceCount, string output)? TryApplyHat6()
    {
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            char c = _buffer[i];
            char lowerC = char.ToLower(c);
            
            if (lowerC is 'a' or 'e' or 'o')
            {
                char newChar = lowerC switch
                {
                    'a' => 'â',
                    'e' => 'ê',
                    'o' => 'ô',
                    _ => c
                };
                
                if (char.IsUpper(c)) newChar = char.ToUpper(newChar);
                
                // Giữ dấu thanh
                var tone = VietnameseChar.GetToneIndex(c);
                if (tone != VietnameseChar.ToneIndex.None)
                    newChar = VietnameseChar.ApplyTone(newChar, tone);
                
                _buffer[i] = newChar;
                
                int backspaceCount = _buffer.Count - i;
                string output = new string(_buffer.Skip(i).ToArray());
                
                return (backspaceCount, output);
            }
            
            if (!IsVowel(c)) break;
        }
        
        return null;
    }
    
    // 7: a->ă, u->ư
    private (int backspaceCount, string output)? TryApplyHat7()
    {
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            char c = _buffer[i];
            char lowerC = char.ToLower(c);
            
            if (lowerC is 'a' or 'u')
            {
                char newChar = lowerC switch
                {
                    'a' => 'ă',
                    'u' => 'ư',
                    _ => c
                };
                
                if (char.IsUpper(c)) newChar = char.ToUpper(newChar);
                
                var tone = VietnameseChar.GetToneIndex(c);
                if (tone != VietnameseChar.ToneIndex.None)
                    newChar = VietnameseChar.ApplyTone(newChar, tone);
                
                _buffer[i] = newChar;
                
                int backspaceCount = _buffer.Count - i;
                string output = new string(_buffer.Skip(i).ToArray());
                
                return (backspaceCount, output);
            }
            
            if (!IsVowel(c)) break;
        }
        
        return null;
    }
    
    // 8: o->ơ
    private (int backspaceCount, string output)? TryApplyHat8()
    {
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            char c = _buffer[i];
            char lowerC = char.ToLower(c);
            
            if (lowerC == 'o')
            {
                char newChar = 'ơ';
                if (char.IsUpper(c)) newChar = char.ToUpper(newChar);
                
                var tone = VietnameseChar.GetToneIndex(c);
                if (tone != VietnameseChar.ToneIndex.None)
                    newChar = VietnameseChar.ApplyTone(newChar, tone);
                
                _buffer[i] = newChar;
                
                int backspaceCount = _buffer.Count - i;
                string output = new string(_buffer.Skip(i).ToArray());
                
                return (backspaceCount, output);
            }
            
            if (!IsVowel(c)) break;
        }
        
        return null;
    }
    
    // 9: d->đ
    private (int backspaceCount, string output)? TryApplyHat9()
    {
        for (int i = _buffer.Count - 1; i >= 0; i--)
        {
            char c = _buffer[i];
            char lowerC = char.ToLower(c);
            
            if (lowerC == 'd')
            {
                char newChar = char.IsUpper(c) ? 'Đ' : 'đ';
                _buffer[i] = newChar;
                
                int backspaceCount = _buffer.Count - i;
                string output = new string(_buffer.Skip(i).ToArray());
                
                return (backspaceCount, output);
            }
        }
        
        return null;
    }
    
    private int FindVowelPositionForTone()
    {
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
        
        if (lastGroup.Count == 2)
        {
            int lastVowelPos = lastGroup[^1];
            bool hasConsonantAfter = lastVowelPos < _buffer.Count - 1 && 
                                     !IsVowel(_buffer[lastVowelPos + 1]);
            
            // Ưu tiên nguyên âm có mũ
            foreach (int pos in lastGroup)
            {
                char c = char.ToLower(_buffer[pos]);
                if (c is 'ê' or 'ô' or 'ơ' or 'â' or 'ă' or 'ư')
                {
                    return pos;
                }
            }
            
            return hasConsonantAfter ? lastGroup[0] : lastGroup[1];
        }
        
        return lastGroup[1];
    }
    
    private bool IsVowel(char c)
    {
        char lower = char.ToLower(c);
        return lower is 'a' or 'ă' or 'â' or 'e' or 'ê' or 'i' or 'o' or 'ô' or 'ơ' or 'u' or 'ư' or 'y'
               || VietnameseChar.IsVietnameseVowel(c);
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
            return false;
        }
        return false;
    }
    
    public string GetBuffer()
    {
        return new string(_buffer.ToArray());
    }
}
