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

    private static readonly Lazy<CvnssConverter> _converter =
        new(() => CvnssConverter.LoadDefault());

    private string _buffer = string.Empty;      // current word buffer (CVNSS input)
    private string _lastOutput = string.Empty;  // last output inserted to target app (CQN)

    // =========================================================
    // REQUIRED BY IInputEngine
    // =========================================================

    public ProcessKeyResult ProcessKey(char key, bool isShiftPressed)
    {
        // Backspace
        if (key == '\b')
            return ProcessBackspace();

        // Enter / newline: reset state and let OS handle.
        if (key == '\r' || key == '\n')
        {
            Reset();
            return NotHandled();
        }

        // Word boundary
        if (IsWordDelimiter(key))
        {
            Reset();
            return NotHandled();
        }

        // Append to buffer and convert
        _buffer += key;

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

    public bool ProcessBackspace()
    {
        if (_buffer.Length == 0)
            return false;

        _buffer = _buffer.Substring(0, _buffer.Length - 1);

        var converted = _buffer.Length == 0
            ? string.Empty
            : SafeConvert(_buffer);

        _lastOutput = converted;
        return true;
    }

    public void Reset()
    {
        _buffer = string.Empty;
        _lastOutput = string.Empty;
    }

    public string GetBuffer()
    {
        return _buffer;
    }

    // =========================================================
    // INTERNAL LOGIC
    // =========================================================

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
        return char.IsWhiteSpace(ch)
            || char.IsPunctuation(ch)
            || char.IsSymbol(ch);
    }

    private static ProcessKeyResult NotHandled()
        => new ProcessKeyResult { Handled = false };
}
