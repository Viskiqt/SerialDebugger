using System.Text;

namespace SerialDebugger
{
    /// <summary>
    /// Turns raw serial bytes into text that is safe to show in the terminal:
    /// UTF-8 decoding (multi-byte characters split across reads are handled),
    /// any line ending (\r\n, \n, \r, \n\r) becomes one line break,
    /// ANSI colour/escape codes are removed, and other control characters are dropped
    /// (a NUL byte would otherwise cut the text off in a Windows text box).
    /// Keeps state between calls; use one instance per connection, from one thread.
    /// </summary>
    internal sealed class TextNormalizer
    {
        private const int MaxEscapeLength = 32;

        private readonly Decoder _decoder = new UTF8Encoding(false, false).GetDecoder();
        private char[] _chars = new char[4096];
        private char _lastBreak;      // '\r' or '\n' if the previous character was a line break
        private int _escapeState;     // 0 = normal text, 1 = after ESC, 2 = inside "ESC [" sequence
        private int _escapeLength;

        public string Process(byte[] data, int count)
        {
            int maxChars = count + 4;
            if (_chars.Length < maxChars)
                _chars = new char[maxChars];

            int n = _decoder.GetChars(data, 0, count, _chars, 0, false);
            var sb = new StringBuilder(n + 16);

            for (int i = 0; i < n; i++)
            {
                char c = _chars[i];

                if (_escapeState == 1)
                {
                    // "ESC [" starts a longer sequence; anything else is a two-character sequence.
                    _escapeState = c == '[' ? 2 : 0;
                    _escapeLength = 0;
                    continue;
                }

                if (_escapeState == 2)
                {
                    // The sequence ends with a character between '@' and '~' (e.g. the 'm' in ESC[32m).
                    if ((c >= '@' && c <= '~') || ++_escapeLength > MaxEscapeLength)
                        _escapeState = 0;
                    continue;
                }

                if (c == '\r' || c == '\n')
                {
                    // Second half of a "\r\n" or "\n\r" pair: already counted.
                    if (_lastBreak != '\0' && _lastBreak != c)
                    {
                        _lastBreak = '\0';
                        continue;
                    }

                    sb.Append("\r\n");
                    _lastBreak = c;
                    continue;
                }

                _lastBreak = '\0';

                if (c == '\x1b')
                {
                    _escapeState = 1;
                    continue;
                }

                if (c == '\t' || (c >= ' ' && c != '\x7f'))
                    sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
