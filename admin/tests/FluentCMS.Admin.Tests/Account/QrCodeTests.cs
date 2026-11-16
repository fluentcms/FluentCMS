using System.Text;
using FluentCMS.Admin.Account;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Account;

// The admin's own QR encoder against the vectors of ISO/IEC 18004 and its tables, and read back by a reader written here
// from the standard, apart from the encoder.
public sealed class QrCodeTests
{
    // Byte capacity at level M (table 7) and the total codewords of each version (table 1).
    private static readonly int[] ByteCapacity = [14, 26, 42, 62, 84, 106, 122, 152, 180, 213, 251, 287, 331, 362, 412];
    private static readonly int[] TotalCodewords = [26, 44, 70, 100, 134, 172, 196, 242, 292, 346, 404, 466, 532, 581, 655];

    [Fact]
    public void TheErrorCorrectionOfTheStandardsExampleIsItsOwn()
    {
        // Annex I: "01234567" as 1-M.
        byte[] data = [0x10, 0x20, 0x0C, 0x56, 0x61, 0x80, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11];

        QrCode.ErrorCorrection(data, 10).ShouldBe([0xA5, 0x24, 0xD4, 0xC1, 0xED, 0x36, 0xC7, 0x87, 0x2C, 0x55]);
    }

    [Fact]
    public void TheErrorCorrectionOfHelloWorldIsTheOneOfThePublishedWalkthrough()
    {
        // "HELLO WORLD" as 1-M in alphanumeric mode, as thonky.com's QR code tutorial works it through.
        byte[] data = [32, 91, 11, 120, 209, 114, 220, 77, 67, 64, 236, 17, 236, 17, 236, 17];

        QrCode.ErrorCorrection(data, 10).ShouldBe([196, 35, 39, 119, 235, 215, 231, 226, 93, 23]);
    }

    [Theory]
    [InlineData(0, "101010000010010")]
    [InlineData(1, "101000100100101")]
    [InlineData(2, "101111001111100")]
    [InlineData(3, "101101101001011")]
    [InlineData(4, "100010111111001")]
    [InlineData(5, "100000011001110")]
    [InlineData(6, "100111110010111")]
    [InlineData(7, "100101010100000")]
    public void TheFormatInformationOfLevelMIsTheStandards(int mask, string bits)
    {
        QrCode.FormatBits(mask).ShouldBe(Convert.ToInt32(bits, 2));
    }

    [Theory]
    [InlineData(7, 0x07C94)]
    [InlineData(8, 0x085BC)]
    [InlineData(9, 0x09A99)]
    [InlineData(10, 0x0A4D3)]
    [InlineData(11, 0x0BBF6)]
    [InlineData(12, 0x0C762)]
    [InlineData(13, 0x0D847)]
    [InlineData(14, 0x0E60D)]
    [InlineData(15, 0x0F928)]
    public void TheVersionInformationIsTheStandards(int version, int bits)
    {
        QrCode.VersionBits(version).ShouldBe(bits);
    }

    public static TheoryData<int> Versions() => [.. Enumerable.Range(1, QrCode.MaxVersion)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void AsManyBytesAsAVersionHoldsTakeItAndOneMoreTakesTheNext(int version)
    {
        var capacity = ByteCapacity[version - 1];

        QrCode.Encode(new byte[capacity]).Version.ShouldBe(version);
        if (version < QrCode.MaxVersion)
        {
            QrCode.Encode(new byte[capacity + 1]).Version.ShouldBe(version + 1);
        }
    }

    [Fact]
    public void MoreThanTheLastVersionHoldsIsRefused()
    {
        Should.Throw<ArgumentException>(() => QrCode.Encode(new byte[ByteCapacity[^1] + 1]));
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void EveryVersionReadsBackWithItsPatternsInPlace(int version)
    {
        // Text that fills most of the version, so that every block carries data.
        var text = string.Concat(Enumerable.Range(0, ByteCapacity[version - 1] - 3).Select(i => (char)('a' + (i % 26))));
        var code = QrCode.Encode(text);

        code.Version.ShouldBe(version);
        code.Size.ShouldBe(17 + (4 * version));
        Reader.Read(code).ShouldBe(text);
    }

    [Fact]
    public void AnAuthenticatorUriReadsBack()
    {
        const string Uri = "otpauth://totp/FluentCMS:ada%40example.com?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=FluentCMS&digits=6";
        var code = QrCode.Encode(Uri);

        code.Version.ShouldBe(7);
        Reader.Read(code).ShouldBe(Uri);
    }

    [Fact]
    public void TheMaskIsTheOneTheFormatInformationNames()
    {
        var code = QrCode.Encode("otpauth://totp/x");

        Reader.Mask(code).ShouldBe(code.Mask);
    }

    // Reads a symbol of level M in byte mode as section 12 of the standard decodes one, with its own tables.
    private static class Reader
    {
        private static readonly int[] EccPerBlock = [10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24];
        private static readonly int[][] Blocks =
        [
            [16], [28], [44], [32, 32], [43, 43], [27, 27, 27, 27], [31, 31, 31, 31], [38, 38, 39, 39], [36, 36, 36, 37, 37],
            [43, 43, 43, 43, 44], [50, 51, 51, 51, 51], [36, 36, 36, 36, 36, 36, 37, 37], [37, 37, 37, 37, 37, 37, 37, 37, 38],
            [40, 40, 40, 40, 41, 41, 41, 41, 41], [41, 41, 41, 41, 41, 42, 42, 42, 42, 42],
        ];

        private static readonly int[][] AlignmentCentres =
        [
            [], [6, 18], [6, 22], [6, 26], [6, 30], [6, 34], [6, 22, 38], [6, 24, 42], [6, 26, 46], [6, 28, 50], [6, 30, 54],
            [6, 32, 58], [6, 34, 62], [6, 26, 46, 66], [6, 26, 48, 70],
        ];

        public static int Mask(QrCode code)
        {
            // The copy beside the upper left finder, bit 14 first.
            List<(int X, int Y)> positions = [(0, 8), (1, 8), (2, 8), (3, 8), (4, 8), (5, 8), (7, 8), (8, 8), (8, 7), (8, 5), (8, 4), (8, 3), (8, 2), (8, 1), (8, 0)];
            var bits = positions.Aggregate(0, (value, position) => (value << 1) | (code[position.X, position.Y] ? 1 : 0)) ^ 0x5412;
            (bits >> 13).ShouldBe(0, "level M");
            return (bits >> 10) & 7;
        }

        public static string Read(QrCode code)
        {
            var version = code.Version;
            var size = code.Size;
            var function = Function(version, size);
            CheckPatterns(code, version);

            var mask = Mask(code);
            List<bool> bits = [];
            for (var right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6)
                {
                    right = 5;
                }

                for (var step = 0; step < size; step++)
                {
                    var upward = ((size - 1 - right) / 2) % 2 == 0;
                    var y = upward ? size - 1 - step : step;
                    foreach (var x in new[] { right, right - 1 })
                    {
                        if (!function[y, x])
                        {
                            bits.Add(code[x, y] ^ Masked(mask, x, y));
                        }
                    }
                }
            }

            (bits.Count / 8).ShouldBe(TotalCodewords[version - 1]);
            var codewords = Enumerable.Range(0, bits.Count / 8)
                .Select(i => (byte)Enumerable.Range(0, 8).Aggregate(0, (value, bit) => (value << 1) | (bits[(i * 8) + bit] ? 1 : 0)))
                .ToArray();

            var lengths = Blocks[version - 1];
            var ecc = EccPerBlock[version - 1];
            var blocks = lengths.Select(length => new List<byte>()).ToList();
            var next = 0;
            for (var i = 0; i < lengths.Max(); i++)
            {
                for (var b = 0; b < blocks.Count; b++)
                {
                    if (i < lengths[b])
                    {
                        blocks[b].Add(codewords[next++]);
                    }
                }
            }

            var corrections = blocks.Select(_ => new List<byte>()).ToList();
            for (var i = 0; i < ecc; i++)
            {
                foreach (var correction in corrections)
                {
                    correction.Add(codewords[next++]);
                }
            }

            next.ShouldBe(codewords.Length);
            for (var b = 0; b < blocks.Count; b++)
            {
                QrCode.ErrorCorrection([.. blocks[b]], ecc).ShouldBe([.. corrections[b]]);
            }

            var data = blocks.SelectMany(block => block).ToArray();
            var reader = new BitReader(data);
            reader.Take(4).ShouldBe(0b0100, "byte mode");
            var count = reader.Take(version < 10 ? 8 : 16);
            var bytes = Enumerable.Range(0, count).Select(_ => (byte)reader.Take(8)).ToArray();
            return Encoding.UTF8.GetString(bytes);
        }

        private static void CheckPatterns(QrCode code, int version)
        {
            var size = code.Size;
            foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
            {
                for (var dy = -3; dy <= 3; dy++)
                {
                    for (var dx = -3; dx <= 3; dx++)
                    {
                        code[cx + dx, cy + dy].ShouldBe(Math.Max(Math.Abs(dx), Math.Abs(dy)) != 2, $"finder at {cx},{cy}");
                    }
                }
            }

            for (var i = 8; i < size - 8; i++)
            {
                code[i, 6].ShouldBe(i % 2 == 0, "timing");
                code[6, i].ShouldBe(i % 2 == 0, "timing");
            }

            foreach (var (ax, ay) in Alignments(version, size))
            {
                code[ax, ay].ShouldBeTrue("alignment centre");
                code[ax + 1, ay].ShouldBeFalse("alignment ring");
                code[ax + 2, ay + 2].ShouldBeTrue("alignment edge");
            }

            code[8, size - 8].ShouldBeTrue("dark module");
        }

        private static IEnumerable<(int X, int Y)> Alignments(int version, int size)
        {
            var centres = AlignmentCentres[version - 1];
            foreach (var x in centres)
            {
                foreach (var y in centres)
                {
                    // Not where a finder is.
                    if (!((x < 9 && y < 9) || (x > size - 10 && y < 9) || (x < 9 && y > size - 10)))
                    {
                        yield return (x, y);
                    }
                }
            }
        }

        private static bool[,] Function(int version, int size)
        {
            var function = new bool[size, size];
            void Mark(int x0, int y0, int width, int height)
            {
                for (var y = y0; y < y0 + height; y++)
                {
                    for (var x = x0; x < x0 + width; x++)
                    {
                        function[y, x] = true;
                    }
                }
            }

            // Finders with separators and format information, the timing patterns and the dark module.
            Mark(0, 0, 9, 9);
            Mark(size - 8, 0, 8, 9);
            Mark(0, size - 8, 9, 8);
            Mark(6, 0, 1, size);
            Mark(0, 6, size, 1);
            foreach (var (x, y) in Alignments(version, size))
            {
                Mark(x - 2, y - 2, 5, 5);
            }

            if (version >= 7)
            {
                Mark(size - 11, 0, 3, 6);
                Mark(0, size - 11, 6, 3);
            }

            return function;
        }

        private static bool Masked(int mask, int x, int y) => mask switch
        {
            0 => (y + x) % 2 == 0,
            1 => y % 2 == 0,
            2 => x % 3 == 0,
            3 => (y + x) % 3 == 0,
            4 => ((y / 2) + (x / 3)) % 2 == 0,
            5 => ((y * x) % 2) + ((y * x) % 3) == 0,
            6 => (((y * x) % 2) + ((y * x) % 3)) % 2 == 0,
            _ => (((y + x) % 2) + ((y * x) % 3)) % 2 == 0,
        };

        private sealed class BitReader(byte[] data)
        {
            private int _position;

            public int Take(int count)
            {
                var value = 0;
                for (var i = 0; i < count; i++, _position++)
                {
                    value = (value << 1) | ((data[_position >> 3] >> (7 - (_position & 7))) & 1);
                }

                return value;
            }
        }
    }
}
