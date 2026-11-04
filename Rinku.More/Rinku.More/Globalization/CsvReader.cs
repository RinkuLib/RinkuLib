using System.Diagnostics.CodeAnalysis;

namespace Rinku.More.Globalization;

/// <summary>
/// Allocation-conscious reader for RFC 4180-style comma-separated rows.
/// </summary>
public sealed class CsvReader(StreamReader reader)
{
    private readonly StreamReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private char[] _buffer = new char[128];

    public bool ReadLine([MaybeNullWhen(false)] out LineReader line)
    {
        if (_reader.Peek() == -1)
        {
            line = default;
            return false;
        }

        line = new LineReader(_reader, ref _buffer);
        return true;
    }

    public ref struct LineReader
    {
        private readonly StreamReader _reader;
        private ref char[] _buffer;
        private bool _ended;
        private int _length;

        internal LineReader(StreamReader reader, ref char[] buffer)
        {
            _reader = reader;
            _buffer = ref buffer;
            _ended = false;
            _length = 0;
        }

        public bool ReadValue([MaybeNullWhen(false)] out ReadOnlySpan<char> value)
        {
            if (_ended)
            {
                value = default;
                return false;
            }

            _length = 0;
            var inQuotes = false;
            var closedQuote = false;
            var complete = false;
            while (!complete)
            {
                var next = _reader.Read();
                if (next == -1)
                {
                    if (inQuotes)
                        throw new InvalidDataException("The CSV file contains an unterminated quoted value.");

                    _ended = true;
                    break;
                }

                var character = (char)next;
                if (inQuotes)
                {
                    if (character != '"')
                    {
                        Append(character);
                    }
                    else if (_reader.Peek() == '"')
                    {
                        _reader.Read();
                        Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        closedQuote = true;
                    }
                }
                else if (closedQuote)
                {
                    if (character == ',')
                    {
                        complete = true;
                    }
                    else if (character == '\r')
                    {
                    }
                    else if (character == '\n')
                    {
                        _ended = true;
                        complete = true;
                    }
                    else
                    {
                        throw new InvalidDataException("The CSV file contains characters after a closing quote.");
                    }
                }
                else if (character == ',')
                {
                    complete = true;
                }
                else if (character == '\r')
                {
                }
                else if (character == '\n')
                {
                    _ended = true;
                    complete = true;
                }
                else if (character == '"')
                {
                    if (_length != 0)
                        throw new InvalidDataException("The CSV file contains a quote in an unquoted value.");

                    inQuotes = true;
                }
                else
                {
                    Append(character);
                }
            }

            value = _buffer.AsSpan(0, _length);
            return true;
        }

        private void Append(char character)
        {
            if (_length == _buffer.Length)
                Array.Resize(ref _buffer, _buffer.Length * 2);

            _buffer[_length++] = character;
        }
    }
}
