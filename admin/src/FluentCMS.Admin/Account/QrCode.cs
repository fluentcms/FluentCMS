using System.Text;

namespace FluentCMS.Admin.Account;

// Derived in part from the QR Code generator library of Project Nayuki (Java), MIT License,
// https://www.nayuki.io/page/qr-code-generator-library; its notice is in THIRD-PARTY-NOTICES.md at the repository root.
// Ported from it: the drawing of the function patterns, format and version information, the placement of the codewords,
// the Reed-Solomon divisor, remainder and multiplication, the masks and the penalty rules. The tables, the byte-mode
// segment, the interleaving and the version range are written here.
//
// A QR code (ISO/IEC 18004) of bytes in byte mode at error correction level M, versions 1 to 15: up to 412 bytes, enough for
// the otpauth URI of an authenticator, so that the page needs no package and no JavaScript; the page draws it as an SVG.
internal sealed class QrCode
{
    public const int MaxVersion = 15;
    public const int MaxBytes = 412;

    // Level M, per version: the error correction codewords of each block, and the blocks of the two groups with the data
    // codewords of each of their blocks (the standard's table 9).
    private static readonly int[] EccPerBlock = [10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24];

    private static readonly (int Blocks, int Data, int MoreBlocks, int MoreData)[] Groups =
    [
        (1, 16, 0, 0), (1, 28, 0, 0), (1, 44, 0, 0), (2, 32, 0, 0), (2, 43, 0, 0), (4, 27, 0, 0), (4, 31, 0, 0),
        (2, 38, 2, 39), (3, 36, 2, 37), (4, 43, 1, 44), (1, 50, 4, 51), (6, 36, 2, 37), (8, 37, 1, 38), (4, 40, 5, 41),
        (5, 41, 5, 42),
    ];

    // The centres of the alignment patterns along each axis (annex E).
    private static readonly int[][] Alignment =
    [
        [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50], [6, 30, 54],
        [6, 32, 58], [6, 34, 62], [6, 26, 46, 66], [6, 26, 48, 70],
    ];

    private readonly bool[,] _dark;
    private readonly bool[,] _function;

    private QrCode(int version)
    {
        Version = version;
        Size = 17 + (4 * version);
        _dark = new bool[Size, Size];
        _function = new bool[Size, Size];
    }

    public int Version { get; }

    public int Size { get; }

    public int Mask { get; private set; }

    public bool this[int x, int y] => _dark[y, x];

    public static QrCode Encode(string text) => Encode(Encoding.UTF8.GetBytes(text));

    public static bool Fits(string text) => Encoding.UTF8.GetByteCount(text) <= MaxBytes;

    // Null for a text longer than MaxBytes in UTF-8.
    public static QrCode? TryEncode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var data = Encoding.UTF8.GetBytes(text);
        return data.Length <= MaxBytes ? Encode(data) : null;
    }

    public static QrCode Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var version = 1;
        while (4 + CountBits(version) + (8 * data.Length) > 8 * DataCodewords(version))
        {
            if (++version > MaxVersion)
            {
                throw new ArgumentException($"{data.Length} bytes do not fit a QR code of version {MaxVersion} at level M.", nameof(data));
            }
        }

        var code = new QrCode(version);
        code.DrawFunctionPatterns();
        code.DrawCodewords(Interleave(version, DataBytes(version, data)));
        code.ChooseMask();
        return code;
    }

    public static int DataCodewords(int version)
    {
        var (blocks, data, moreBlocks, moreData) = Groups[version - 1];
        return (blocks * data) + (moreBlocks * moreData);
    }

    // The error correction codewords of a block (Reed-Solomon over GF(256) with the polynomial 0x11D, annex A).
    public static byte[] ErrorCorrection(ReadOnlySpan<byte> data, int count)
    {
        var generator = new byte[count];
        generator[count - 1] = 1;
        byte root = 1;
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                generator[j] = Multiply(generator[j], root);
                if (j + 1 < count)
                {
                    generator[j] ^= generator[j + 1];
                }
            }

            root = Multiply(root, 0x02);
        }

        var remainder = new byte[count];
        foreach (var value in data)
        {
            var factor = (byte)(value ^ remainder[0]);
            Array.Copy(remainder, 1, remainder, 0, count - 1);
            remainder[count - 1] = 0;
            for (var j = 0; j < count; j++)
            {
                remainder[j] ^= Multiply(generator[j], factor);
            }
        }

        return remainder;
    }

    // The 15 bits of the format information for level M and the mask, with their BCH code and the standard's XOR mask.
    public static int FormatBits(int mask)
    {
        var data = mask; // level M is 00
        var remainder = data;
        for (var i = 0; i < 10; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }

        return ((data << 10) | remainder) ^ 0x5412;
    }

    // The 18 bits of the version information of versions 7 and up.
    public static int VersionBits(int version)
    {
        var remainder = version;
        for (var i = 0; i < 12; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
        }

        return (version << 12) | remainder;
    }

    public static bool Masked(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => ((x / 3) + (y / 2)) % 2 == 0,
        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
        _ => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
    };

    private static int CountBits(int version) => version < 10 ? 8 : 16;

    // Mode, count, the bytes, the terminator, then the pad codewords 0xEC and 0x11 in turn.
    private static byte[] DataBytes(int version, byte[] data)
    {
        var capacity = DataCodewords(version);
        var bits = new List<bool>(capacity * 8);
        Append(bits, 0b0100, 4);
        Append(bits, data.Length, CountBits(version));
        foreach (var value in data)
        {
            Append(bits, value, 8);
        }

        Append(bits, 0, Math.Min(4, (capacity * 8) - bits.Count));
        Append(bits, 0, (8 - (bits.Count % 8)) % 8);

        var bytes = new byte[capacity];
        for (var i = 0; i < bits.Count; i++)
        {
            if (bits[i])
            {
                bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
            }
        }

        for (int i = bits.Count / 8, pad = 0; i < capacity; i++, pad++)
        {
            bytes[i] = pad % 2 == 0 ? (byte)0xEC : (byte)0x11;
        }

        return bytes;
    }

    private static void Append(List<bool> bits, int value, int length)
    {
        for (var i = length - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) != 0);
        }
    }

    // The data codewords split into blocks, each with its error correction, then read across the blocks (8.6).
    private static byte[] Interleave(int version, byte[] data)
    {
        var (blocks, size, moreBlocks, moreSize) = Groups[version - 1];
        var ecc = EccPerBlock[version - 1];
        List<byte[]> dataBlocks = [];
        List<byte[]> eccBlocks = [];
        var offset = 0;
        for (var i = 0; i < blocks + moreBlocks; i++)
        {
            var length = i < blocks ? size : moreSize;
            var block = data.AsSpan(offset, length).ToArray();
            offset += length;
            dataBlocks.Add(block);
            eccBlocks.Add(ErrorCorrection(block, ecc));
        }

        List<byte> result = new(data.Length + (ecc * dataBlocks.Count));
        for (var i = 0; i < Math.Max(size, moreSize); i++)
        {
            result.AddRange(dataBlocks.Where(block => i < block.Length).Select(block => block[i]));
        }

        for (var i = 0; i < ecc; i++)
        {
            result.AddRange(eccBlocks.Select(block => block[i]));
        }

        return [.. result];
    }

    private static byte Multiply(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }

        return (byte)z;
    }

    private void DrawFunctionPatterns()
    {
        for (var i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var centres = Alignment[Version - 1];
        var last = centres.Length - 1;
        for (var i = 0; i < centres.Length; i++)
        {
            for (var j = 0; j < centres.Length; j++)
            {
                if (!((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0)))
                {
                    DrawAlignment(centres[i], centres[j]);
                }
            }
        }

        // Reserves the format areas; the mask chosen later writes them.
        DrawFormat(0);
        if (Version >= 7)
        {
            var bits = VersionBits(Version);
            for (var i = 0; i < 18; i++)
            {
                var dark = ((bits >> i) & 1) != 0;
                var a = Size - 11 + (i % 3);
                var b = i / 3;
                SetFunction(a, b, dark);
                SetFunction(b, a, dark);
            }
        }
    }

    private void DrawFinder(int x, int y)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (x + dx >= 0 && x + dx < Size && y + dy >= 0 && y + dy < Size)
                {
                    SetFunction(x + dx, y + dy, distance != 2 && distance != 4);
                }
            }
        }
    }

    private void DrawAlignment(int x, int y)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }
    }

    // Both copies of the format information, and the dark module beside the lower one.
    private void DrawFormat(int mask)
    {
        var bits = FormatBits(mask);
        bool Bit(int i) => ((bits >> i) & 1) != 0;

        for (var i = 0; i <= 5; i++)
        {
            SetFunction(8, i, Bit(i));
        }

        SetFunction(8, 7, Bit(6));
        SetFunction(8, 8, Bit(7));
        SetFunction(7, 8, Bit(8));
        for (var i = 9; i < 15; i++)
        {
            SetFunction(14 - i, 8, Bit(i));
        }

        for (var i = 0; i < 8; i++)
        {
            SetFunction(Size - 1 - i, 8, Bit(i));
        }

        for (var i = 8; i < 15; i++)
        {
            SetFunction(8, Size - 15 + i, Bit(i));
        }

        SetFunction(8, Size - 8, true);
    }

    // Upward and downward in columns of two from the right, skipping the vertical timing pattern (7.7.3). Modules left over
    // are the remainder bits, which are light.
    private void DrawCodewords(byte[] codewords)
    {
        var i = 0;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            var upward = ((right + 1) & 2) == 0;
            for (var vertical = 0; vertical < Size; vertical++)
            {
                var y = upward ? Size - 1 - vertical : vertical;
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    if (!_function[y, x] && i < codewords.Length * 8)
                    {
                        _dark[y, x] = ((codewords[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
            }
        }
    }

    // The mask of the lowest penalty (7.8.3).
    private void ChooseMask()
    {
        var best = 0;
        var lowest = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormat(mask);
            var penalty = Penalty();
            if (penalty < lowest)
            {
                (best, lowest) = (mask, penalty);
            }

            ApplyMask(mask);
        }

        Mask = best;
        ApplyMask(best);
        DrawFormat(best);
    }

    private void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!_function[y, x] && Masked(mask, x, y))
                {
                    _dark[y, x] = !_dark[y, x];
                }
            }
        }
    }

    private int Penalty() => Penalty(_dark);

    // The four rules of 7.8.3 as the library above scores them: runs of five or more of a colour (N1 3, then 1 a module),
    // 2 by 2 blocks of a colour (N2 3), finder-like 1:1:3:1:1 runs with four light modules on a side, the border counting as
    // light (N3 40), and 10 for each 5 % step of the dark share away from 45 to 55 % (N4).
    internal static int Penalty(bool[,] modules)
    {
        var size = modules.GetLength(0);
        var penalty = 0;
        for (var line = 0; line < size; line++)
        {
            penalty += LinePenalty(size, i => modules[line, i]) + LinePenalty(size, i => modules[i, line]);
        }

        for (var y = 0; y < size - 1; y++)
        {
            for (var x = 0; x < size - 1; x++)
            {
                var colour = modules[y, x];
                if (colour == modules[y, x + 1] && colour == modules[y + 1, x] && colour == modules[y + 1, x + 1])
                {
                    penalty += 3;
                }
            }
        }

        var dark = 0;
        foreach (var module in modules)
        {
            dark += module ? 1 : 0;
        }

        var total = size * size;
        var k = ((Math.Abs((dark * 20) - (total * 10)) + total - 1) / total) - 1;
        return penalty + (k * 10);
    }

    // N1 and N3 along one row or column, from the lengths of its last seven runs, newest first, light first.
    private static int LinePenalty(int size, Func<int, bool> module)
    {
        var penalty = 0;
        var colour = false;
        var run = 0;
        var history = new int[7];
        for (var i = 0; i < size; i++)
        {
            if (module(i) == colour)
            {
                run++;
                penalty += run == 5 ? 3 : run > 5 ? 1 : 0;
            }
            else
            {
                AddRun(size, run, history);
                if (!colour)
                {
                    penalty += FinderLike(history) * 40;
                }

                colour = module(i);
                run = 1;
            }
        }

        if (colour)
        {
            AddRun(size, run, history);
            run = 0;
        }

        AddRun(size, run + size, history);
        return penalty + (FinderLike(history) * 40);
    }

    // The first run of a line also holds the light border before it.
    private static void AddRun(int size, int run, int[] history)
    {
        if (history[0] == 0)
        {
            run += size;
        }

        Array.Copy(history, 0, history, 1, history.Length - 1);
        history[0] = run;
    }

    private static int FinderLike(int[] history)
    {
        var n = history[1];
        var core = n > 0 && history[2] == n && history[3] == n * 3 && history[4] == n && history[5] == n;
        return (core && history[0] >= n * 4 && history[6] >= n ? 1 : 0) + (core && history[6] >= n * 4 && history[0] >= n ? 1 : 0);
    }

    private void SetFunction(int x, int y, bool dark)
    {
        _dark[y, x] = dark;
        _function[y, x] = true;
    }
}
