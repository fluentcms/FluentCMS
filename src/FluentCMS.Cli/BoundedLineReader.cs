using System.Text;

namespace FluentCMS.Cli;

// The lines of standard input or of an event stream, each at most a bound long: a longer line is read to its end and given
// as too long, without its text, so that what is held stays bounded. A line ends at a line feed, a carriage return or both,
// as TextReader.ReadLine has it. Each read takes what is there: StreamReader.ReadAsync goes on reading while every read
// fills its buffer, so on a pipe whose writer waits for an answer it would wait for bytes that never come.
internal sealed class BoundedLineReader
{
    private readonly Func<Memory<char>, CancellationToken, ValueTask<int>> _read;
    private readonly int _maxLength;
    private readonly char[] _buffer = new char[8192];
    private int _start;
    private int _end;
    private bool _ended;

    // A line that ended with a carriage return owns the line feed that may follow it.
    private bool _afterCarriageReturn;

    private BoundedLineReader(Func<Memory<char>, CancellationToken, ValueTask<int>> read, int maxLength)
    {
        _read = read;
        _maxLength = maxLength;
    }

    public static BoundedLineReader Of(TextReader reader, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new BoundedLineReader(reader.ReadAsync, maxLength);
    }

    // UTF-8, decoded here, with a byte order mark at the start left out.
    public static BoundedLineReader Of(Stream stream, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new BoundedLineReader(new Utf8Chunks(stream).ReadAsync, maxLength);
    }

    // Null once the source has ended and every line is read.
    public async ValueTask<BoundedLine?> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new StringBuilder();
        var tooLong = false;
        var any = false;
        while (true)
        {
            if (_start == _end)
            {
                if (_ended)
                {
                    return any ? Line(line, tooLong) : null;
                }

                _start = 0;
                _end = await _read(_buffer, cancellationToken);
                _ended = _end == 0;
                continue;
            }

            if (_afterCarriageReturn)
            {
                _afterCarriageReturn = false;
                if (_buffer[_start] == '\n')
                {
                    _start++;
                    continue;
                }
            }

            any = true;
            var span = _buffer.AsSpan(_start, _end - _start);
            var end = span.IndexOfAny('\r', '\n');
            var part = end < 0 ? span : span[..end];
            if (!tooLong && line.Length + part.Length > _maxLength)
            {
                tooLong = true;
                line.Clear();
            }
            else if (!tooLong)
            {
                line.Append(part);
            }

            _start += part.Length;
            if (end < 0)
            {
                continue;
            }

            _afterCarriageReturn = _buffer[_start] == '\r';
            _start++;
            return Line(line, tooLong);
        }
    }

    private static BoundedLine Line(StringBuilder line, bool tooLong) =>
        tooLong ? new BoundedLine("", TooLong: true) : new BoundedLine(line.ToString(), TooLong: false);

    // One read of the stream, decoded. A read gives at most two characters more than its bytes, when it completes a
    // sequence of four bytes whose first ones came before, so it asks for two bytes fewer than there is room for.
    private sealed class Utf8Chunks(Stream stream)
    {
        private const char ByteOrderMark = (char)0xFEFF;

        private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();
        private readonly byte[] _bytes = new byte[8192];
        private bool _started;

        public async ValueTask<int> ReadAsync(Memory<char> chars, CancellationToken cancellationToken)
        {
            while (true)
            {
                var read = await stream.ReadAsync(_bytes.AsMemory(0, Math.Min(_bytes.Length, chars.Length - 2)), cancellationToken);
                var count = _decoder.GetChars(_bytes.AsSpan(0, read), chars.Span, flush: read == 0);
                if (!_started && count > 0)
                {
                    _started = true;
                    if (chars.Span[0] == ByteOrderMark)
                    {
                        chars.Span[1..count].CopyTo(chars.Span);
                        count--;
                    }
                }

                // A read that ended inside a sequence gives no character yet, and the rest of it is on its way.
                if (count > 0 || read == 0)
                {
                    return count;
                }
            }
        }
    }
}

internal readonly record struct BoundedLine(string Text, bool TooLong);
