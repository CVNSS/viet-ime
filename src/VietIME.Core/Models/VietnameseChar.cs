namespace VietIME.Core.Models;

/// <summary>
/// Bảng mã ký tự tiếng Việt theo chuẩn Unicode
/// </summary>
public static class VietnameseChar
{
    // Nguyên âm cơ bản và các biến thể có dấu
    public static readonly Dictionary<char, char[]> VowelMap = new()
    {
        // a -> à á ả ã ạ
        ['a'] = ['a', 'à', 'á', 'ả', 'ã', 'ạ'],
        ['A'] = ['A', 'À', 'Á', 'Ả', 'Ã', 'Ạ'],
        
        // ă -> ằ ắ ẳ ẵ ặ
        ['ă'] = ['ă', 'ằ', 'ắ', 'ẳ', 'ẵ', 'ặ'],
        ['Ă'] = ['Ă', 'Ằ', 'Ắ', 'Ẳ', 'Ẵ', 'Ặ'],
        
        // â -> ầ ấ ẩ ẫ ậ
        ['â'] = ['â', 'ầ', 'ấ', 'ẩ', 'ẫ', 'ậ'],
        ['Â'] = ['Â', 'Ầ', 'Ấ', 'Ẩ', 'Ẫ', 'Ậ'],
        
        // e -> è é ẻ ẽ ẹ
        ['e'] = ['e', 'è', 'é', 'ẻ', 'ẽ', 'ẹ'],
        ['E'] = ['E', 'È', 'É', 'Ẻ', 'Ẽ', 'Ẹ'],
        
        // ê -> ề ế ể ễ ệ
        ['ê'] = ['ê', 'ề', 'ế', 'ể', 'ễ', 'ệ'],
        ['Ê'] = ['Ê', 'Ề', 'Ế', 'Ể', 'Ễ', 'Ệ'],
        
        // i -> ì í ỉ ĩ ị
        ['i'] = ['i', 'ì', 'í', 'ỉ', 'ĩ', 'ị'],
        ['I'] = ['I', 'Ì', 'Í', 'Ỉ', 'Ĩ', 'Ị'],
        
        // o -> ò ó ỏ õ ọ
        ['o'] = ['o', 'ò', 'ó', 'ỏ', 'õ', 'ọ'],
        ['O'] = ['O', 'Ò', 'Ó', 'Ỏ', 'Õ', 'Ọ'],
        
        // ô -> ồ ố ổ ỗ ộ
        ['ô'] = ['ô', 'ồ', 'ố', 'ổ', 'ỗ', 'ộ'],
        ['Ô'] = ['Ô', 'Ồ', 'Ố', 'Ổ', 'Ỗ', 'Ộ'],
        
        // ơ -> ờ ớ ở ỡ ợ
        ['ơ'] = ['ơ', 'ờ', 'ớ', 'ở', 'ỡ', 'ợ'],
        ['Ơ'] = ['Ơ', 'Ờ', 'Ớ', 'Ở', 'Ỡ', 'Ợ'],
        
        // u -> ù ú ủ ũ ụ
        ['u'] = ['u', 'ù', 'ú', 'ủ', 'ũ', 'ụ'],
        ['U'] = ['U', 'Ù', 'Ú', 'Ủ', 'Ũ', 'Ụ'],
        
        // ư -> ừ ứ ử ữ ự
        ['ư'] = ['ư', 'ừ', 'ứ', 'ử', 'ữ', 'ự'],
        ['Ư'] = ['Ư', 'Ừ', 'Ứ', 'Ử', 'Ữ', 'Ự'],
        
        // y -> ỳ ý ỷ ỹ ỵ
        ['y'] = ['y', 'ỳ', 'ý', 'ỷ', 'ỹ', 'ỵ'],
        ['Y'] = ['Y', 'Ỳ', 'Ý', 'Ỷ', 'Ỹ', 'Ỵ'],
    };

    // Chỉ số dấu: 0=không dấu, 1=huyền, 2=sắc, 3=hỏi, 4=ngã, 5=nặng
    public enum ToneIndex
    {
        None = 0,   // Không dấu
        Grave = 1,  // Huyền (`)
        Acute = 2,  // Sắc (´)
        Hook = 3,   // Hỏi (?)
        Tilde = 4,  // Ngã (~)
        Dot = 5     // Nặng (.)
    }

    // Chuyển đổi nguyên âm cơ bản
    public static readonly Dictionary<char, char> BaseVowelTransform = new()
    {
        // a -> ă, â
        ['a'] = 'a', ['ă'] = 'a', ['â'] = 'a',
        ['A'] = 'A', ['Ă'] = 'A', ['Â'] = 'A',
        
        // e -> ê
        ['e'] = 'e', ['ê'] = 'e',
        ['E'] = 'E', ['Ê'] = 'E',
        
        // o -> ô, ơ
        ['o'] = 'o', ['ô'] = 'o', ['ơ'] = 'o',
        ['O'] = 'O', ['Ô'] = 'O', ['Ơ'] = 'O',
        
        // u -> ư
        ['u'] = 'u', ['ư'] = 'u',
        ['U'] = 'U', ['Ư'] = 'U',
    };

    // Phụ âm đặc biệt
    public const char LowerD = 'đ';
    public const char UpperD = 'Đ';

    /// <summary>
    /// Kiểm tra ký tự có phải nguyên âm tiếng Việt không
    /// </summary>
    public static bool IsVietnameseVowel(char c)
    {
        return VowelMap.ContainsKey(c) || 
               VowelMap.Values.Any(arr => arr.Contains(c));
    }

    /// <summary>
    /// Lấy nguyên âm gốc (không dấu, không mũ)
    /// </summary>
    public static char GetBaseVowel(char c)
    {
        // Tìm trong map
        foreach (var kvp in VowelMap)
        {
            if (kvp.Value.Contains(c))
            {
                // Lấy base vowel
                if (BaseVowelTransform.TryGetValue(kvp.Key, out var baseVowel))
                {
                    return baseVowel;
                }
                return kvp.Key;
            }
        }
        return c;
    }
    
    /// <summary>
    /// Lấy nguyên âm không có dấu thanh (giữ mũ/móc)
    /// Ví dụ: ệ -> ê, ố -> ô, ấ -> â
    /// </summary>
    public static char GetVowelWithoutTone(char c)
    {
        foreach (var kvp in VowelMap)
        {
            if (kvp.Value.Contains(c))
            {
                // Trả về nguyên âm không dấu (index 0)
                return kvp.Value[0];
            }
        }
        return c;
    }

    /// <summary>
    /// Lấy index của dấu thanh
    /// </summary>
    public static ToneIndex GetToneIndex(char c)
    {
        foreach (var kvp in VowelMap)
        {
            for (int i = 0; i < kvp.Value.Length; i++)
            {
                if (kvp.Value[i] == c)
                {
                    return (ToneIndex)i;
                }
            }
        }
        return ToneIndex.None;
    }

    /// <summary>
    /// Áp dụng dấu thanh cho nguyên âm
    /// </summary>
    public static char ApplyTone(char vowel, ToneIndex tone)
    {
        // Tìm vowel trong map
        foreach (var kvp in VowelMap)
        {
            if (kvp.Value.Contains(vowel))
            {
                int index = (int)tone;
                if (index >= 0 && index < kvp.Value.Length)
                {
                    return kvp.Value[index];
                }
            }
        }
        return vowel;
    }

    /// <summary>
    /// Chuyển đổi nguyên âm (a -> ă, a -> â, etc.)
    /// </summary>
    public static char TransformVowel(char vowel, char modifier)
    {
        bool isUpper = char.IsUpper(vowel);
        char lowerVowel = char.ToLower(vowel);
        char lowerMod = char.ToLower(modifier);
        
        // Lấy dấu thanh hiện tại
        ToneIndex currentTone = GetToneIndex(vowel);
        
        char result = vowel;
        
        // Xử lý chuyển đổi
        switch (lowerMod)
        {
            case 'w': // ă, ơ, ư
                result = lowerVowel switch
                {
                    'a' or 'ă' or 'â' => 'ă',
                    'o' or 'ô' or 'ơ' => 'ơ',
                    'u' or 'ư' => 'ư',
                    _ => vowel
                };
                break;
                
            case 'a': // â (aa)
                if (lowerVowel == 'a' || lowerVowel == 'ă' || lowerVowel == 'â')
                {
                    result = 'â';
                }
                break;
                
            case 'e': // ê (ee)
                if (lowerVowel == 'e' || lowerVowel == 'ê')
                {
                    result = 'ê';
                }
                break;
                
            case 'o': // ô (oo)
                if (lowerVowel == 'o' || lowerVowel == 'ô' || lowerVowel == 'ơ')
                {
                    result = 'ô';
                }
                break;
        }
        
        // Giữ nguyên case
        if (isUpper)
        {
            result = char.ToUpper(result);
        }
        
        // Áp dụng lại dấu thanh
        if (currentTone != ToneIndex.None && VowelMap.ContainsKey(result))
        {
            result = ApplyTone(result, currentTone);
        }
        
        return result;
    }
}
