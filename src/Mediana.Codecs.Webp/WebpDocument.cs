// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Formats.Metadata;

namespace Mediana.Codecs.Webp
{
    internal class WebpDocument
    {
        internal int Width, Height;
        internal long Duration;
        internal uint Background;
        internal int LoopCount;
        internal bool Animated;
        internal ReadOnlyMemory<byte> Icc, Exif, Xmp;
        internal OrthogonalTransform Orientation { get; }
        private ReadOnlyMemory<byte> ExifTiff => Exif.Span.StartsWith("Exif\0\0"u8) ? Exif[6..] : Exif;
        internal readonly List<WebpDecodedFrame> Frames = [];
        internal WebpDocument(ReadOnlyMemory<byte> encoded, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (encoded.Length < 12 || !encoded.Span.StartsWith("RIFF"u8) || !encoded.Span.Slice(8, 4).SequenceEqual("WEBP"u8)
                || BinaryPrimitives.ReadUInt32LittleEndian(encoded.Span[4..]) != encoded.Length - 8)
            { throw new InvalidDataException("Invalid WebP RIFF extent."); }
            int flags = -1; bool animationHeader = false;
            ReadOnlyMemory<byte> image = default, alpha = default;
            foreach (WebpChunk chunk in Chunks(encoded[12..], token))
            {
                switch (chunk.Type)
                {
                    case "VP8X":
                        if (flags >= 0 || Frames.Count != 0 || !image.IsEmpty || chunk.Payload.Length != 10)
                        { throw new InvalidDataException("Invalid WebP extended header order or size."); }
                        flags = chunk.Payload.Span[0];
                        if ((flags & 0xc1) != 0 || chunk.Payload.Span.Slice(1, 3).IndexOfAnyExcept((byte)0) >= 0)
                        { throw new InvalidDataException("Invalid WebP extended header reserved bits."); }
                        Width = Read24(chunk.Payload.Span[4..]) + 1; Height = Read24(chunk.Payload.Span[7..]) + 1;
                        Animated = (flags & 2) != 0;
                        if ((long)Width * Height > uint.MaxValue) { throw new InvalidDataException("WebP canvas exceeds its specified pixel range."); }
                        break;
                    case "ICCP": Unique(ref Icc, chunk.Payload); break;
                    case "EXIF": Unique(ref Exif, chunk.Payload); break;
                    case "XMP ": Unique(ref Xmp, chunk.Payload); break;
                    case "ANIM":
                        if (!Animated || animationHeader || chunk.Payload.Length != 6 || Frames.Count != 0)
                        { throw new InvalidDataException("Invalid WebP animation header."); }
                        Background = BinaryPrimitives.ReadUInt32LittleEndian(chunk.Payload.Span);
                        LoopCount = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Payload.Span[4..]); animationHeader = true;
                        break;
                    case "ANMF":
                        if (!animationHeader || chunk.Payload.Length < 16) { throw new InvalidDataException("Invalid WebP animation frame."); }
                        ReadOnlySpan<byte> header = chunk.Payload.Span;
                        int x = Read24(header) * 2, y = Read24(header[3..]) * 2;
                        int w = Read24(header[6..]) + 1, h = Read24(header[9..]) + 1, delay = Read24(header[12..]);
                        if ((header[15] & ~3) != 0 || x > Width - w || y > Height - h)
                        { throw new InvalidDataException("WebP animation rectangle exceeds its canvas."); }
                        ReadOnlyMemory<byte> frameImage = default, frameAlpha = default; bool lossless = false;
                        foreach (WebpChunk part in Chunks(chunk.Payload[16..], token))
                        {
                            switch (part.Type)
                            {
                                case "ALPH":
                                    if (!frameImage.IsEmpty) { throw new InvalidDataException("WebP alpha must precede its image."); }
                                    Unique(ref frameAlpha, part.Payload); break;
                                case "VP8 ": Unique(ref frameImage, part.Payload); break;
                                case "VP8L": Unique(ref frameImage, part.Payload); lossless = true; break;
                            }
                        }
                        CheckImage(frameImage, frameAlpha, lossless, w, h);
                        Frames.Add(new(x, y, w, h, delay, (header[15] & 2) != 0, (header[15] & 1) != 0, lossless, frameImage, frameAlpha));
                        Duration = checked(Duration + delay); break;
                    case "ALPH":
                        if (Animated || !image.IsEmpty) { throw new InvalidDataException("WebP alpha must precede a still image."); }
                        Unique(ref alpha, chunk.Payload); break;
                    case "VP8 ":
                    case "VP8L":
                        Unique(ref image, chunk.Payload);
                        bool stillLossless = chunk.Type == "VP8L";
                        (int stillWidth, int stillHeight) = Dimensions(image, stillLossless);
                        if (flags < 0) { Width = stillWidth; Height = stillHeight; }
                        CheckImage(image, alpha, stillLossless, Width, Height);
                        if (Animated) { throw new InvalidDataException("Animated WebP has a top-level still image."); }
                        Frames.Add(new(0, 0, Width, Height, 0, true, false, stillLossless, image, alpha));
                        break;
                }
            }
            if (Frames.Count == 0 || (Animated && !animationHeader)) { throw new InvalidDataException("WebP has no image frames."); }
            if (flags < 0 && (!Icc.IsEmpty || !Exif.IsEmpty || !Xmp.IsEmpty || !alpha.IsEmpty))
            { throw new InvalidDataException("WebP features require an extended header."); }
            if (flags >= 0 && (((flags & 32) != 0) != !Icc.IsEmpty || ((flags & 8) != 0) != !Exif.IsEmpty || ((flags & 4) != 0) != !Xmp.IsEmpty))
            { throw new InvalidDataException("WebP metadata flags disagree with its chunks."); }
            Orientation = Exif.IsEmpty ? OrthogonalTransform.Identity : ExifOrientation.Read(ExifTiff, token);
        }
        private static void Unique(ref ReadOnlyMemory<byte> destination, ReadOnlyMemory<byte> value)
        {
            if (!destination.IsEmpty || value.IsEmpty) { throw new InvalidDataException("Duplicate or empty WebP feature chunk."); }
            destination = value;
        }
        private static void CheckImage(ReadOnlyMemory<byte> image, ReadOnlyMemory<byte> alpha, bool lossless, int width, int height)
        {
            if (image.IsEmpty) { throw new InvalidDataException("WebP frame has no encoded image."); }
            if (lossless && !alpha.IsEmpty) { throw new InvalidDataException("Lossless WebP cannot use an external alpha chunk."); }
            if (Dimensions(image, lossless) != (width, height)) { throw new InvalidDataException("WebP image dimensions disagree with its canvas or frame."); }
        }
        internal static (int Width, int Height) Dimensions(ReadOnlyMemory<byte> image, bool lossless)
        {
            ReadOnlySpan<byte> data = image.Span;
            if (lossless)
            {
                if (data.Length < 5 || data[0] != 47) { throw new InvalidDataException("Invalid VP8L image header."); }
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
                if (bits >> 29 != 0) { throw new InvalidDataException("Unsupported VP8L version."); }
                return ((int)(bits & 16383) + 1, (int)((bits >> 14) & 16383) + 1);
            }
            if (data.Length < 10 || (data[0] & 1) != 0 || (data[0] & 16) == 0 || !data.Slice(3, 3).SequenceEqual(new byte[] { 157, 1, 42 }))
            { throw new InvalidDataException("WebP requires a displayed VP8 keyframe."); }
            int width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 16383, height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 16383;
            if (width == 0 || height == 0) { throw new InvalidDataException("Invalid VP8 image dimensions."); }
            return (width, height);
        }
        internal static IEnumerable<WebpChunk> Chunks(ReadOnlyMemory<byte> data, CancellationToken token)
        {
            int offset = 0;
            while (offset < data.Length)
            {
                token.ThrowIfCancellationRequested();
                if (data.Length - offset < 8) { throw new InvalidDataException("Truncated WebP chunk header."); }
                uint size = BinaryPrimitives.ReadUInt32LittleEndian(data.Span[(offset + 4)..]);
                if ((ulong)size + (size & 1) > (ulong)(data.Length - offset - 8)) { throw new InvalidDataException("Truncated WebP chunk payload."); }
                if ((size & 1) != 0 && data.Span[offset + 8 + (int)size] != 0) { throw new InvalidDataException("Invalid WebP chunk padding."); }
                string type = System.Text.Encoding.ASCII.GetString(data.Span.Slice(offset, 4));
                yield return new(type, data.Slice(offset + 8, (int)size));
                offset += checked(8 + (int)size + (int)(size & 1));
            }
        }
        internal static int Read24(ReadOnlySpan<byte> data) => data[0] | data[1] << 8 | data[2] << 16;
        internal ImageInfo Inspect(CancellationToken token)
        {
            List<MediaProperty> properties = [new("media", "codec", Frames.All(frame => frame.Lossless) ? "vp8l" : Frames.All(frame => !frame.Lossless) ? "vp8" : "vp8/vp8l"),
                new("webp", "animated", Animated), new("webp", "frame_count", Frames.Count), new("webp", "stored_dimensions", Width, Height)];
            if (Animated) { properties.AddRange([new("webp", "loop_count", LoopCount), new("webp", "background_bgra", Background)]); }
            if (!Icc.IsEmpty) { properties.Add(new("icc", "profile", MediaPropertyValue.FromBytes(Icc.Span))); }
            if (!Xmp.IsEmpty) { properties.Add(new("xmp", "packet", MediaPropertyValue.FromBytes(Xmp.Span))); }
            List<MediaMetadata> children = [];
            if (!Exif.IsEmpty)
            {
                children.Add(ExifMetadataReader.Read(ExifTiff, token));
            }
            if (Animated)
            {
                for (int i = 0; i < Frames.Count; i++)
                {
                    WebpDecodedFrame frame = Frames[i];
                    children.Add(new("frame/" + i, [new("webp", "rectangle", frame.Left, frame.Top, frame.Width, frame.Height),
                        new("webp", "duration_ms", frame.Duration), new("webp", "replace", frame.Replace), new("webp", "dispose_to_background", frame.Dispose)]));
                }
            }
            return new("webp", Orientation.SwapsAxes ? Height : Width, Orientation.SwapsAxes ? Width : Height,
                ImageDimensionUnit.Pixels, duration: Animated ? new(Duration, new(1, 1000)) : null,
                metadata: new("image", properties, children));
        }
    }
}
