// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Buffers.Binary;
using System.Text;

namespace Mediana.Codecs.OpenExr
{
    internal class ExrHeader
    {
        internal Dictionary<string, ExrAttribute> Attributes { get; } = new(StringComparer.Ordinal);
        internal List<ExrChannel> Channels { get; } = [];
        internal ExrWindow DataWindow { get; private set; }
        internal ExrWindow DisplayWindow { get; private set; }
        internal int Compression { get; private set; }
        internal int LineOrder { get; private set; }
        internal float PixelAspectRatio { get; private set; }
        internal string Name { get; private set; } = string.Empty;
        internal string Type { get; private set; } = "scanlineimage";
        internal int ChunkCount { get; private set; }
        internal ReadOnlyMemory<byte> Offsets { get; set; }
        internal int LinesPerChunk => Compression switch
        {
            0 or 1 or 2 => 1,
            3 or 5 => 16,
            4 or 6 or 7 or 8 or 11 => 32,
            9 or 10 => 256,
            _ => throw new NotSupportedException($"OpenEXR compression {Compression} is not supported.")
        };

        internal static ExrHeader Read(ref ExrCursor cursor, int nameLength, bool extended, bool tiled, CancellationToken token)
        {
            ExrHeader result = new();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                string name = cursor.Text(nameLength);
                if (name.Length == 0) { break; }
                string type = cursor.Text(nameLength);
                if (type.Length == 0) { throw new InvalidDataException("Empty OpenEXR attribute type."); }
                ReadOnlyMemory<byte> data = cursor.Take(cursor.Int());
                if (!result.Attributes.TryAdd(name, new(type, data))) { throw new InvalidDataException("Duplicate OpenEXR attribute."); }
            }
            result.DataWindow = ExrWindow.Read(result.Required("dataWindow", "box2i"));
            result.DisplayWindow = ExrWindow.Read(result.Required("displayWindow", "box2i"));
            result.Compression = result.ByteAttribute("compression", "compression");
            result.LineOrder = result.ByteAttribute("lineOrder", "lineOrder");
            if (result.LineOrder > 2) { throw new InvalidDataException("Invalid OpenEXR scan line order."); }
            result.PixelAspectRatio = result.FloatAttribute("pixelAspectRatio");
            if (!float.IsFinite(result.PixelAspectRatio) || result.PixelAspectRatio <= 0) { throw new InvalidDataException("Invalid OpenEXR pixel aspect ratio."); }
            float screenWidth = result.FloatAttribute("screenWindowWidth");
            if (result.Required("screenWindowCenter", "v2f").Length != 8 || !float.IsFinite(screenWidth) || screenWidth < 0)
            { throw new InvalidDataException("Invalid OpenEXR screen window."); }
            result.ReadChannels(nameLength, token);
            if (result.Attributes.TryGetValue("name", out ExrAttribute partName))
            { result.Name = TextAttribute(partName); }
            if (result.Attributes.TryGetValue("type", out ExrAttribute partType)) { result.Type = TextAttribute(partType); }
            else if (tiled) { result.Type = "tiledimage"; }
            if (extended && (result.Name.Length == 0 || !result.Attributes.ContainsKey("type")))
            { throw new InvalidDataException("Multipart and deep OpenEXR require a name and type for every part."); }
            if (result.Type is "scanlineimage" or "deepscanline" && result.LineOrder == 2)
            { throw new InvalidDataException("OpenEXR scanline parts cannot declare random line order."); }
            if (result.Attributes.TryGetValue("chunkCount", out ExrAttribute chunks))
            {
                if (chunks.Type != "int" || chunks.Data.Length != 4) { throw new InvalidDataException("Invalid OpenEXR chunk count."); }
                result.ChunkCount = BinaryPrimitives.ReadInt32LittleEndian(chunks.Data.Span);
                if (result.ChunkCount <= 0) { throw new InvalidDataException("OpenEXR chunk count must be positive."); }
            }
            else if (extended) { throw new InvalidDataException("Multipart or deep OpenEXR chunk count is missing."); }
            else if (result.Type == "scanlineimage") { result.ChunkCount = (result.DataWindow.Height - 1) / result.LinesPerChunk + 1; }
            else { throw new NotSupportedException("This OpenEXR part layout requires a declared chunk count."); }
            return result;
        }

        internal ReadOnlyMemory<byte> Required(string name, string type)
        {
            if (!Attributes.TryGetValue(name, out ExrAttribute attribute) || attribute.Type != type)
            { throw new InvalidDataException($"OpenEXR requires '{name}' with type '{type}'."); }
            return attribute.Data;
        }
        private int ByteAttribute(string name, string type)
        {
            ReadOnlyMemory<byte> bytes = Required(name, type);
            if (bytes.Length != 1) { throw new InvalidDataException("Invalid OpenEXR byte attribute."); }
            return bytes.Span[0];
        }
        private float FloatAttribute(string name)
        {
            ReadOnlyMemory<byte> bytes = Required(name, "float");
            if (bytes.Length != 4) { throw new InvalidDataException("Invalid OpenEXR float attribute."); }
            return BinaryPrimitives.ReadSingleLittleEndian(bytes.Span);
        }
        private static string TextAttribute(ExrAttribute attribute)
        {
            if (attribute.Type != "string") { throw new InvalidDataException("Invalid OpenEXR string attribute."); }
            return new UTF8Encoding(false, true).GetString(attribute.Data.Span);
        }
        private void ReadChannels(int maximumNameLength, CancellationToken token)
        {
            ReadOnlyMemory<byte> bytes = Required("channels", "chlist");
            ExrCursor cursor = new(bytes);
            HashSet<string> names = new(StringComparer.Ordinal);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int start = cursor.Position;
                string name = cursor.Text(maximumNameLength);
                if (name.Length == 0) { break; }
                ReadOnlyMemory<byte> encodedName = bytes.Slice(start, cursor.Position - start - 1);
                int type = cursor.Int(), linear = cursor.Byte();
                if (type is < 0 or > 2 || linear > 1 || cursor.Take(3).Span.ContainsAnyExcept((byte)0))
                { throw new InvalidDataException("Invalid OpenEXR channel declaration."); }
                int samplingX = cursor.Int(), samplingY = cursor.Int();
                if (samplingX <= 0 || samplingY <= 0 || !names.Add(name)) { throw new InvalidDataException("Invalid or duplicate OpenEXR channel."); }
                Channels.Add(new(name, encodedName, type, linear != 0, samplingX, samplingY));
            }
            if (Channels.Count == 0 || cursor.Remaining != 0) { throw new InvalidDataException("Invalid OpenEXR channel list."); }
            Channels.Sort((left, right) => left.EncodedName.Span.SequenceCompareTo(right.EncodedName.Span));
        }
    }
}
