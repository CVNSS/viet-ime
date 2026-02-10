using System;
using VietIME.Core.Cvnss;
using VietIME.Core.Models;

namespace VietIME.Core.Engines;

/// <summary>
/// CVNSS4.0 input engine.
/// 
/// IMPORTANT:
/// - This file intentionally keeps the class name TelexEngine so VietIME's existing UI ("Telex") keeps working
///   without changing XAML or settings code. In the UI, the engine name will show as "CVNSS4.0".
/// - Example: typing "Xin chaol" becomes "Xin chào".
/// </summary>
public sealed class TelexEngine : IInputEngine
{
    public string Name => "CVNSS4.0";

    private static readonly Lazy<CvnssConverter> _converter = new(() => CvnssConverter.LoadDefault());

    private string _buffer = string.Empty;      // current word buffer (CVNSS input)
    private string _lastOutput = string.Empty;  // last output inserted to target app (CQN)

    public ProcessKeyResult ProcessKey(string key, bool isShiftPressed)
    {
        if (string.IsNullOrEmpty(key))
            return NotHandled();

        // Backspace: we handle only if we have an active buffer to edit.
        if (key.Length == 1 && key[0] == '\b')
            return ProcessBackspace();

        // Enter / newline: reset state and let OS handle.
        if (key.Length == 1 && (key[0] == '\r' || key[0] == '\n'))
        {
            Reset();
            return NotHandled();
        }

        // We only process single printable characters. Anything else -> reset and pass through.
        if (key.Length != 1)
        {
            Reset();
            return NotHandled();
        }

        var ch = key[0];

        // Word boundary -> commit previous word (already committed via _lastOutput) then reset buffer and pass through.
        if (IsWordDelimiter(ch))
        {
            Reset();
            return NotHandled();
        }

        // Append to buffer and convert
        _buffer += ch;

        var converted = SafeConvert(_buffer);

        var result = new ProcessKeyResult
        {
            Handled = true,
            BackspaceCount = _lastOutput.Length,
            OutputText = converted,
            CurrentBuffer = _buffer
        };

        _lastOutput = converted;
        return result;
    }

    private ProcessKeyResult ProcessBackspace()
    {
        if (_buffer.Length == 0)
            return NotHandled();

        _buffer = _buffer.Substring(0, _buffer.Length - 1);

        var converted = _buffer.Length == 0 ? string.Empty : SafeConvert(_buffer);

        var result = new ProcessKeyResult
        {
            Handled = true,
            BackspaceCount = _lastOutput.Length,
            OutputText = converted,
            CurrentBuffer = _buffer
        };

        _lastOutput = converted;
        return result;
    }

    private static string SafeConvert(string cvnWord)
    {
        try
        {
            // Convert only current word (CVN -> CQN)
            return _converter.Value.ConvertWordCvnToCqn(cvnWord);
        }
        catch
        {
            // Fail-open: if mapping missing or parsing error, do not block typing.
            return cvnWord;
        }
    }

    private static bool IsWordDelimiter(char ch)
    {
        // Anything that is whitespace/punctuation/symbol ends the word.
        // You can relax this if you want hyphen/apostrophe inside words.
        return char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch);
    }

    private void Reset()
    {
        _buffer = string.Empty;
        _lastOutput = string.Empty;
    }

    private static ProcessKeyResult NotHandled() => new ProcessKeyResult { Handled = false };
}
