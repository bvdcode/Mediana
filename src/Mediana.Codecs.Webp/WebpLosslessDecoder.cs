// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Runtime.InteropServices;

using Mediana.Core.Memory;

namespace Mediana.Codecs.Webp
{
    internal static partial class WebpLosslessDecoder
    {
        internal static void Decode(ReadOnlySpan<byte> encoded, Span<uint> pixels, int width, int height, MediaBufferWorkspace workspace, CancellationToken token)
        {
            if (encoded.Length < 5 || encoded[0] != 47) { throw new InvalidDataException("Invalid VP8L header."); }
            WebpBitReader input = new(encoded[5..]);
            DecodeImage(ref input, pixels, width, height, workspace, token);
        }
        internal static void DecodeImage(ref WebpBitReader input, Span<uint> pixels, int width, int height, MediaBufferWorkspace workspace, CancellationToken token)
        {
            int codedWidth = width, seen = 0;
            List<WebpLosslessTransform> transforms = new(4);
            while (input.Read(1) != 0)
            {
                int type = input.Read(2);
                if ((seen & (1 << type)) != 0) { throw new InvalidDataException("Duplicate VP8L transform."); }
                seen |= 1 << type;
                switch (type)
                {
                    case 0:
                    case 1:
                        int bits = input.Read(3) + 2;
                        int columns = Subsample(codedWidth, bits), rows = Subsample(height, bits);
                        Memory<byte> data = workspace.Allocate(checked(columns * rows * 4));
                        ReadImage(ref input, MemoryMarshal.Cast<byte, uint>(data.Span), columns, rows, false, workspace, token);
                        transforms.Add(new(type, codedWidth, bits, data)); break;
                    case 2: transforms.Add(new(type, codedWidth, 0, default)); break;
                    case 3:
                        int count = input.Read(8) + 1;
                        Memory<byte> paletteData = workspace.Allocate(256 * 4);
                        Span<uint> palette = MemoryMarshal.Cast<byte, uint>(paletteData.Span);
                        ReadImage(ref input, palette[..count], count, 1, false, workspace, token);
                        for (int i = 1; i < count; i++) { palette[i] = Add(palette[i], palette[i - 1]); }
                        int packed = count switch { <= 2 => 3, <= 4 => 2, <= 16 => 1, _ => 0 };
                        transforms.Add(new(type, codedWidth, packed, paletteData)); codedWidth = Subsample(codedWidth, packed); break;
                    default: throw new InvalidDataException("Invalid VP8L transform.");
                }
            }
            ReadImage(ref input, pixels[..checked(codedWidth * height)], codedWidth, height, true, workspace, token);
            for (int index = transforms.Count - 1; index >= 0; index--) { Inverse(pixels, height, transforms[index], token); }
        }
        private static void ReadImage(ref WebpBitReader input, Span<uint> output, int width, int height, bool main, MediaBufferWorkspace workspace, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            int cacheBits = 0;
            if (input.Read(1) != 0)
            {
                cacheBits = input.Read(4);
                if (cacheBits is < 1 or > 11) { throw new InvalidDataException("Invalid VP8L color cache size."); }
            }
            int cacheSize = cacheBits == 0 ? 0 : 1 << cacheBits;
            Memory<byte> cacheMemory = cacheSize == 0 ? default : workspace.Allocate(cacheSize * 4);
            Span<uint> cache = MemoryMarshal.Cast<byte, uint>(cacheMemory.Span);
            int prefixBits = 0, prefixWidth = 0, groupCount = 1;
            Memory<byte> entropyMemory = default;
            if (main && input.Read(1) != 0)
            {
                prefixBits = input.Read(3) + 2; prefixWidth = Subsample(width, prefixBits);
                int rows = Subsample(height, prefixBits);
                entropyMemory = workspace.Allocate(checked(prefixWidth * rows * 4));
                Span<uint> entropy = MemoryMarshal.Cast<byte, uint>(entropyMemory.Span);
                ReadImage(ref input, entropy, prefixWidth, rows, false, workspace, token);
                foreach (uint value in entropy) { groupCount = Math.Max(groupCount, (int)((value >> 8) & 65535) + 1); }
            }
            WebpPrefixCode[] codes = new WebpPrefixCode[checked(groupCount * 5)];
            for (int group = 0; group < groupCount; group++)
            {
                token.ThrowIfCancellationRequested();
                for (int channel = 0; channel < 5; channel++)
                { codes[group * 5 + channel] = WebpPrefixCode.Read(ref input, channel switch { 0 => 280 + cacheSize, 4 => 40, _ => 256 }, workspace); }
            }
            ReadOnlySpan<uint> meta = MemoryMarshal.Cast<byte, uint>(entropyMemory.Span);
            int nextCancellation = 0;
            for (int position = 0; position < output.Length;)
            {
                if (position >= nextCancellation) { token.ThrowIfCancellationRequested(); nextCancellation = position + Math.Min(16384, output.Length - position); }
                int group = meta.IsEmpty ? 0 : (int)((meta[(position / width >> prefixBits) * prefixWidth + (position % width >> prefixBits)] >> 8) & 65535) * 5;
                int symbol = codes[group].Decode(ref input);
                if (symbol < 256)
                {
                    uint red = (uint)codes[group + 1].Decode(ref input), blue = (uint)codes[group + 2].Decode(ref input), alpha = (uint)codes[group + 3].Decode(ref input);
                    uint color = blue | ((uint)symbol << 8) | (red << 16) | (alpha << 24);
                    output[position++] = color;
                    if (cacheBits != 0) { cache[(int)(unchecked(0x1e35a7bdu * color) >> (32 - cacheBits))] = color; }
                }
                else if (symbol < 280)
                {
                    int count = Length(ref input, symbol - 256), code = Length(ref input, codes[group + 4].Decode(ref input));
                    int distance = Distance(code, width);
                    if (distance > position || count > output.Length - position) { throw new InvalidDataException("VP8L backreference exceeds decoded pixels."); }
                    for (int copy = 0; copy < count; copy++)
                    {
                        uint color = output[position - distance]; output[position++] = color;
                        if (cacheBits != 0) { cache[(int)(unchecked(0x1e35a7bdu * color) >> (32 - cacheBits))] = color; }
                    }
                }
                else
                {
                    int index = symbol - 280;
                    if (index >= cache.Length) { throw new InvalidDataException("Invalid VP8L color cache index."); }
                    uint color = cache[index]; output[position++] = color;
                    cache[(int)(unchecked(0x1e35a7bdu * color) >> (32 - cacheBits))] = color;
                }
            }
        }
        private static int Length(ref WebpBitReader input, int code)
        {
            if (code < 4) { return code + 1; }
            int extra = (code - 2) / 2;
            return ((2 + (code & 1)) << extra) + input.Read(extra) + 1;
        }
        private static int Subsample(int value, int bits) => (value + (1 << bits) - 1) >> bits;
        private static int Distance(int code, int width)
        {
            if (code > 120) { return code - 120; }
            (int x, int y) = Distances[code - 1];
            return Math.Max(1, x + y * width);
        }
        private static readonly (int X, int Y)[] Distances = [(0, 1), (1, 0), (1, 1), (-1, 1), (0, 2), (2, 0), (1, 2), (-1, 2), (2, 1), (-2, 1), (2, 2), (-2, 2), (0, 3), (3, 0), (1, 3), (-1, 3), (3, 1), (-3, 1), (2, 3), (-2, 3), (3, 2), (-3, 2), (0, 4), (4, 0), (1, 4), (-1, 4), (4, 1), (-4, 1), (3, 3), (-3, 3), (2, 4), (-2, 4), (4, 2), (-4, 2), (0, 5), (3, 4), (-3, 4), (4, 3), (-4, 3), (5, 0), (1, 5), (-1, 5), (5, 1), (-5, 1), (2, 5), (-2, 5), (5, 2), (-5, 2), (4, 4), (-4, 4), (3, 5), (-3, 5), (5, 3), (-5, 3), (0, 6), (6, 0), (1, 6), (-1, 6), (6, 1), (-6, 1), (2, 6), (-2, 6), (6, 2), (-6, 2), (4, 5), (-4, 5), (5, 4), (-5, 4), (3, 6), (-3, 6), (6, 3), (-6, 3), (0, 7), (7, 0), (1, 7), (-1, 7), (5, 5), (-5, 5), (7, 1), (-7, 1), (4, 6), (-4, 6), (6, 4), (-6, 4), (2, 7), (-2, 7), (7, 2), (-7, 2), (3, 7), (-3, 7), (7, 3), (-7, 3), (5, 6), (-5, 6), (6, 5), (-6, 5), (8, 0), (4, 7), (-4, 7), (7, 4), (-7, 4), (8, 1), (8, 2), (6, 6), (-6, 6), (8, 3), (5, 7), (-5, 7), (7, 5), (-7, 5), (8, 4), (6, 7), (-6, 7), (7, 6), (-7, 6), (8, 5), (7, 7), (-7, 7), (8, 6), (8, 7)];
    }
}
