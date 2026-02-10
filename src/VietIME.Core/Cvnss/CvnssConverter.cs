using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Reflection;

namespace VietIME.Core.Cvnss;

/// <summary>
/// CVNSS4.0 converter (CVN -> CQN) ported from cvnss4.0-converter.js.
/// Load mapping data from a JSON file (cvnss4.0-map.json).
/// </summary>
public sealed class CvnssConverter
{
    private readonly HashSet<string> _specialTokens;
    private readonly List<string> _consonantsCvn;  // ordered
    private readonly List<string> _consonantsCqn;  // aligned
    private readonly Dictionary<string, string> _vowelCvnToCqn;
    private readonly string[] _baseVowels;
    private readonly string _iSet;
    private readonly string _ySet;
    private readonly string _nguyenAmSet;

    private CvnssConverter(
        IEnumerable<string> specialTokens,
        IList<string> consonantsCvn,
        IList<string> consonantsCqn,
        IDictionary<string, string> vowelCvnToCqn,
        string[] baseVowels,
        string iSet,
        string ySet,
        string nguyenAmSet)
    {
        _specialTokens = new HashSet<string>(specialTokens);
        _consonantsCvn = consonantsCvn.ToList();
        _consonantsCqn = consonantsCqn.ToList();
        _vowelCvnToCqn = new Dictionary<string, string>(vowelCvnToCqn);
        _baseVowels = baseVowels;
        _iSet = iSet;
        _ySet = ySet;
        _nguyenAmSet = nguyenAmSet;
    }

    
    /// <summary>
    /// Load converter using the standard mapping file name (cvnss4.0-map.json).
    /// It tries:
    /// 1) AppContext.BaseDirectory\cvnss4.0-map.json
    /// 2) Embedded resource that ends with "cvnss4.0-map.json"
    /// </summary>
    public static CvnssConverter LoadDefault()
    {
        var baseDirPath = Path.Combine(AppContext.BaseDirectory, "cvnss4.0-map.json");
        if (File.Exists(baseDirPath))
        {
            return LoadFromJson(baseDirPath);
        }

        var asm = typeof(CvnssConverter).Assembly;
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("cvnss4.0-map.json", StringComparison.OrdinalIgnoreCase));

        if (resName is null)
        {
            throw new FileNotFoundException(
                "Cannot locate cvnss4.0-map.json. Please ensure it's copied next to the executable " +
                "or embedded as a resource in VietIME.Core.");
        }

        using var stream = asm.GetManifestResourceStream(resName);
        if (stream is null) throw new FileNotFoundException($"Resource stream not found: {resName}");

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var json = reader.ReadToEnd();
        return LoadFromJsonString(json);
    }

    public static CvnssConverter LoadFromJsonString(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var specialChars = root.GetProperty("specialChars").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

        var consonants = root.GetProperty("consonants");
        var consonantsCqn = consonants.GetProperty("cqn").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        var consonantsCvn = consonants.GetProperty("cvn").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

        var vowels = root.GetProperty("vowels");
        var vowelsCqn = vowels.GetProperty("cqn").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        var vowelsCvn = vowels.GetProperty("cvn").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

        var vowelDict = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < Math.Min(vowelsCqn.Length, vowelsCvn.Length); i++)
        {
            var k = vowelsCvn[i];
            var v = vowelsCqn[i];
            if (!string.IsNullOrEmpty(k)) vowelDict[k] = v;
        }

        var baseVowels = root.GetProperty("baseVowels").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

        var sets = root.GetProperty("sets");
        var iSet = sets.GetProperty("I").GetString() ?? "iI";
        var ySet = sets.GetProperty("Y").GetString() ?? "yY";
        var nguyenAmSet = sets.GetProperty("nguyenAm").GetString() ?? "";

        return new CvnssConverter(
            specialTokens: specialChars,
            consonantsCvn: consonantsCvn,
            consonantsCqn: consonantsCqn,
            vowelCvnToCqn: vowelDict,
            baseVowels: baseVowels,
            iSet: iSet,
            ySet: ySet,
            nguyenAmSet: nguyenAmSet);
    }

public static CvnssConverter LoadFromJson(string jsonPath)
    {
        var json = File.ReadAllText(jsonPath, Encoding.UTF8);
        var doc = JsonDocument.Parse(json);

        var special = doc.RootElement.GetProperty("specialChars").EnumerateArray().Select(x => x.GetString() ?? "").ToList();

        var consonants = doc.RootElement.GetProperty("consonants");
        var cqn = consonants.GetProperty("cqn").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
        var cvn = consonants.GetProperty("cvn").EnumerateArray().Select(x => x.GetString() ?? "").ToList();

        var vowels = doc.RootElement.GetProperty("vowels");
        var vowelsCqn = vowels.GetProperty("cqn").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        var vowelsCvn = vowels.GetProperty("cvn").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();

        var vowelMap = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < vowelsCvn.Length; i++)
        {
            // first occurrence wins (consistent with JS indexOf)
            if (!vowelMap.ContainsKey(vowelsCvn[i]))
                vowelMap[vowelsCvn[i]] = vowelsCqn[i];
        }

        var baseVowels = doc.RootElement.GetProperty("baseVowels").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();

        var repl = doc.RootElement.GetProperty("specialReplacements");
        var ySet = repl.GetProperty("y").GetString() ?? "yỳỷỹýỵ";
        var iSet = repl.GetProperty("i").GetString() ?? "iìỉĩíị";

        var adj = doc.RootElement.GetProperty("consonantAdjustments");
        var nguyenAmSet = adj.GetProperty("nguyen_am").GetString() ?? "ieê";

        // IMPORTANT: CVN consonants include duplicates like "w". We must keep order and keep aligned CQN.
        return new CvnssConverter(
            specialTokens: special,
            consonantsCvn: cvn.OrderByDescending(s => s.Length).ToList(), // ensure longest match first
            consonantsCqn: AlignConsonants(cvn, cqn),
            vowelCvnToCqn: vowelMap,
            baseVowels: baseVowels,
            iSet: iSet,
            ySet: ySet,
            nguyenAmSet: nguyenAmSet
        );
    }

    /// <summary>
    /// Convert a single word typed in CVN to CQN (Vietnamese with diacritics).
    /// Preserves capitalization similar to the JS implementation.
    /// </summary>
    public string ConvertWordCvnToCqn(string word)
    {
        if (string.IsNullOrEmpty(word)) return word;

        var lower = word.ToLowerInvariant();

        string consonant = "";
        string vowelPart = lower;
        string cqnConsonant = "";

        // Find consonant (match by prefix; longest first)
        for (int i = 0; i < _consonantsCvn.Count; i++)
        {
            var cvnCon = _consonantsCvn[i];
            if (lower.StartsWith(cvnCon, StringComparison.Ordinal))
            {
                consonant = cvnCon;
                cqnConsonant = MapConsonantCvnToCqn(cvnCon);
                vowelPart = lower.Substring(cvnCon.Length);
                break;
            }
        }

        // Map vowel part
        if (!_vowelCvnToCqn.TryGetValue(vowelPart, out var cqnVowel))
        {
            cqnVowel = vowelPart; // fallback
        }

        // Special case in JS
        if (consonant == "j" && vowelPart == "ịa")
        {
            cqnConsonant = "gi";
            cqnVowel = "ỵa";
        }

        // Adjust consonant/vowel like JS adjustConsonantVowel
        (cqnConsonant, cqnVowel) = AdjustConsonantVowel(cqnConsonant, cqnVowel);

        var cqnOutput = cqnConsonant + cqnVowel;

        // Preserve capitalization
        if (IsAllUpper(word))
            return cqnOutput.ToUpperInvariant();

        if (char.IsLetter(word[0]) && char.IsUpper(word[0]))
        {
            if (cqnOutput.Length > 0)
                cqnOutput = char.ToUpperInvariant(cqnOutput[0]) + cqnOutput.Substring(1);
        }

        return cqnOutput;
    }

    /// <summary>
    /// Optional helper: convert a full string by splitting on special tokens (space/punctuation) like the JS splitString.
    /// </summary>
    public string ConvertTextCvnToCqn(string input)
    {
        if (input == null) return "";
        input = input.Normalize(NormalizationForm.FormC);

        var tokens = SplitStringLikeJs(input);
        var sb = new StringBuilder(input.Length);

        foreach (var t in tokens)
        {
            if (_specialTokens.Contains(t))
                sb.Append(t);
            else
                sb.Append(ConvertWordCvnToCqn(t));
        }
        return sb.ToString();
    }

    private (string Pad, string Van) AdjustConsonantVowel(string cqnPad, string cqnVan)
    {
        var firstChar = cqnVan.Length > 0 ? cqnVan[0] : '\0';
        var baseVowel = GetBaseVowel(firstChar);

        // qu + u* => q
        if (cqnPad == "qu" && baseVowel == 'u')
            cqnPad = "q";

        // No consonant + i* => use y* (Vietnamese orthography)
        if (string.IsNullOrEmpty(cqnPad) && baseVowel == 'i')
        {
            var idx = _iSet.IndexOf(firstChar);
            if (idx >= 0 && idx < _ySet.Length)
                cqnVan = _ySet[idx] + cqnVan.Substring(1);
        }

        // gi + i* => g
        if (cqnPad == "gi" && baseVowel == 'i')
            cqnPad = "g";

        // ngh/gh/k adjust if vowel base not in ieê
        if ((cqnPad == "ngh" || cqnPad == "gh" || cqnPad == "k") && _nguyenAmSet.IndexOf(baseVowel) < 0)
        {
            cqnPad = cqnPad switch
            {
                "ngh" => "ng",
                "gh" => "g",
                "k" => "c",
                _ => cqnPad
            };
        }

        return (cqnPad, cqnVan);
    }

    private char GetBaseVowel(char ch)
    {
        foreach (var grp in _baseVowels)
        {
            if (grp.IndexOf(ch) >= 0)
                return grp[0];
        }
        return ch;
    }

    private static bool IsAllUpper(string s)
    {
        // Similar to JS: uppercase A-Z + Vietnamese uppercase letters
        // This is a practical approximation: consider a string "all upper" if it has
        // at least one letter and every letter is uppercase.
        bool hasLetter = false;
        foreach (var ch in s)
        {
            if (char.IsLetter(ch))
            {
                hasLetter = true;
                if (!char.IsUpper(ch)) return false;
            }
        }
        return hasLetter;
    }

    private string MapConsonantCvnToCqn(string cvnConsonant)
    {
        // Because cvn list can have duplicates ("w"), we replicate JS behavior:
        // take the FIRST index in the original consonants.cvn list (after ordering by length,
        // duplicates still map consistently through this method).
        for (int i = 0; i < _consonantsCvn.Count; i++)
        {
            if (_consonantsCvn[i] == cvnConsonant)
                return _consonantsCqn[i];
        }
        return cvnConsonant;
    }

    private static List<string> AlignConsonants(IList<string> cvn, IList<string> cqn)
    {
        // Keep mapping aligned with cvn AFTER sorting by length:
        // We'll create pairs, then sort them the same way, preserving original order among equals.
        var pairs = cvn.Select((v, i) => (cvn: v, cqn: cqn[i], i)).ToList();
        return pairs
            .OrderByDescending(p => p.cvn.Length)
            .ThenBy(p => p.i)
            .Select(p => p.cqn)
            .ToList();
    }

    private static List<string> SplitStringLikeJs(string s)
    {
        // Equivalent (approx) to JS regex split:
        // /([\s|,|;|`|@|<|>|“|”|.|=|…|?|!|\\|'|"|(|)|[|\]|{|}|%|#|$|&|\-|_|/|*|:|+|~|^|||\\r\\n|\\n|\\r])/gm
        // We'll treat any whitespace or common punctuation as delimiter tokens.
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch) || IsSplitPunctuation(ch))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                tokens.Add(ch.ToString());
            }
            else
            {
                current.Append(ch);
            }
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());

        return tokens;
    }

    private static bool IsSplitPunctuation(char ch)
    {
        return ch switch
        {
            ',' or ';' or '`' or '@' or '<' or '>' or '“' or '”' or '.' or '=' or '…' or '?' or '!' or '\\'
                or '\'' or '"' or '(' or ')' or '[' or ']' or '{' or '}' or '%' or '#' or '$' or '&' or '-'
                or '_' or '/' or '*' or ':' or '+' or '~' or '^' or '|' => true,
            _ => false
        };
    }
}
