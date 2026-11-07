// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Vp9
{
    internal partial class Vp9FrameHeader
    {
        internal Vp9PictureGeometry Geometry { get; private set; }
        internal bool KeyFrame { get; private set; }
        internal bool IsIntra { get; private set; }
        internal bool ShowFrame { get; private set; }
        internal bool ShowExisting { get; private set; }
        internal int ExistingReference { get; private set; }
        internal bool ErrorResilient { get; private set; }
        internal bool ParallelDecoding { get; private set; }
        internal bool RefreshContext { get; private set; }
        internal bool HighPrecisionMotion { get; private set; }
        internal int ResetContext { get; private set; }
        internal int SelectedContext { get; private set; }
        internal int Context => IsIntra || ErrorResilient ? 0 : SelectedContext;
        internal int RefreshReferences { get; private set; }
        internal int InterpolationFilter { get; private set; }
        internal int CompressedOffset { get; private set; }
        internal int CompressedLength { get; private set; }
        internal readonly int[] ReferenceIndices = new int[3], SignBias = new int[4];

        internal static Vp9FrameHeader Parse(ReadOnlySpan<byte> encoded, Vp9HeaderState state)
        {
            Vp9FrameHeader result = new();
            Vp9BitReader bits = new(encoded);
            result.Read(ref bits, state);
            bits.Align();
            result.CompressedOffset = bits.ByteOffset;
            if (!result.ShowExisting && (result.CompressedLength == 0 || result.CompressedLength >= encoded.Length - bits.ByteOffset))
            { throw new InvalidDataException("The VP9 compressed header or tile payload extent is invalid."); }
            if (result.ShowExisting && bits.ByteOffset != encoded.Length && encoded[bits.ByteOffset..].IndexOfAnyExcept((byte)0) >= 0)
            { throw new InvalidDataException("VP9 show-existing padding must be zero."); }
            return result;
        }

        private void Read(ref Vp9BitReader bits, Vp9HeaderState state)
        {
            if (bits.Read(2) != 2) { throw new InvalidDataException("Invalid VP9 frame marker."); }
            int profile = bits.Read(1) | bits.Read(1) << 1;
            if (profile == 3) { RequireZero(ref bits); }
            ShowExisting = bits.Flag();
            if (ShowExisting)
            {
                ExistingReference = bits.Read(3); Geometry = state.Reference(ExistingReference);
                if (Geometry.Profile != profile) { throw new InvalidDataException("The VP9 displayed reference profile differs from the frame header."); }
                ShowFrame = true; return;
            }
            KeyFrame = !bits.Flag(); ShowFrame = bits.Flag(); ErrorResilient = bits.Flag();
            Vp9PictureGeometry geometry;
            if (KeyFrame)
            {
                ReadSync(ref bits); geometry = ReadColor(ref bits, profile);
                geometry = ReadSize(ref bits, geometry); RefreshReferences = 255; IsIntra = true;
            }
            else
            {
                IsIntra = !ShowFrame && bits.Flag();
                ResetContext = ErrorResilient ? 0 : bits.Read(2);
                if (IsIntra)
                {
                    ReadSync(ref bits);
                    geometry = profile > 0 ? ReadColor(ref bits, profile) : new(0, 0, 0, 0, 0, 8, 1, false, true, true);
                    RefreshReferences = bits.Read(8); geometry = ReadSize(ref bits, geometry);
                }
                else
                {
                    RefreshReferences = bits.Read(8);
                    for (int i = 0; i < 3; i++) { ReferenceIndices[i] = bits.Read(3); SignBias[i + 1] = bits.Read(1); }
                    geometry = state.Reference(ReferenceIndices[0]);
                    if (geometry.Profile != profile) { throw new InvalidDataException("The VP9 reference profile differs from the frame header."); }
                    bool found = false;
                    for (int i = 0; i < 3; i++)
                    {
                        Vp9PictureGeometry reference = state.Reference(ReferenceIndices[i]);
                        if (!SameSampling(geometry, reference)) { throw new InvalidDataException("VP9 references disagree on precision, color or sampling."); }
                        if (!found && bits.Flag()) { geometry = geometry with { Width = reference.Width, Height = reference.Height }; found = true; break; }
                    }
                    if (!found) { geometry = ReadDimensions(ref bits, geometry); }
                    geometry = ReadRender(ref bits, geometry);
                    bool validScale = false;
                    for (int i = 0; i < 3; i++)
                    {
                        Vp9PictureGeometry reference = state.Reference(ReferenceIndices[i]);
                        if (!SameSampling(geometry, reference)) { throw new InvalidDataException("VP9 references disagree on precision, color or sampling."); }
                        validScale |= geometry.Width * 2 >= reference.Width && geometry.Height * 2 >= reference.Height &&
                            geometry.Width <= reference.Width * 16 && geometry.Height <= reference.Height * 16;
                    }
                    if (!validScale) { throw new InvalidDataException("No VP9 reference is within the permitted scaling range."); }
                    HighPrecisionMotion = bits.Flag(); InterpolationFilter = bits.Flag() ? 4 : bits.Read(2) switch
                    {
                        0 => 1, 1 => 0, 2 => 2, 3 => 3, _ => throw new InvalidDataException("Invalid VP9 interpolation filter.")
                    };
                }
            }
            Geometry = geometry;
            if (!ErrorResilient) { RefreshContext = bits.Flag(); ParallelDecoding = bits.Flag(); }
            else { ParallelDecoding = true; }
            SelectedContext = bits.Read(2);
            if (IsIntra || ErrorResilient) { state.ResetIndependence(); Array.Clear(SignBias); }
            ReadFilter(ref bits, state); ReadQuantizers(ref bits); ReadSegmentation(ref bits, state); ReadTiles(ref bits);
            CompressedLength = bits.Read(16);
        }

        private static bool SameSampling(Vp9PictureGeometry a, Vp9PictureGeometry b) => a.Profile == b.Profile && a.BitDepth == b.BitDepth &&
            a.ColorSpace == b.ColorSpace && a.SubsamplingX == b.SubsamplingX && a.SubsamplingY == b.SubsamplingY;

        private static void ReadSync(ref Vp9BitReader bits)
        { if (bits.Read(24) != 0x498342) { throw new InvalidDataException("Invalid VP9 frame synchronization code."); } }
        private static void RequireZero(ref Vp9BitReader bits)
        { if (bits.Flag()) { throw new InvalidDataException("A VP9 reserved bit must be zero."); } }
        private static Vp9PictureGeometry ReadColor(ref Vp9BitReader bits, int profile)
        {
            int depth = profile >= 2 ? bits.Flag() ? 12 : 10 : 8, color = bits.Read(3);
            bool full = true, sx = false, sy = false;
            if (color == 6) { throw new NotSupportedException("The VP9 color space is reserved."); }
            if (color == 7)
            {
                if ((profile & 1) == 0) { throw new InvalidDataException("VP9 RGB requires an odd-numbered profile."); }
                RequireZero(ref bits);
            }
            else
            {
                full = bits.Flag();
                if ((profile & 1) != 0)
                {
                    sx = bits.Flag(); sy = bits.Flag(); RequireZero(ref bits);
                    if (sx && sy) { throw new InvalidDataException("VP9 odd-numbered profiles do not permit 4:2:0 chroma."); }
                }
                else { sx = sy = true; }
            }
            return new(0, 0, 0, 0, profile, depth, color, full, sx, sy);
        }
        private static Vp9PictureGeometry ReadSize(ref Vp9BitReader bits, Vp9PictureGeometry geometry) => ReadRender(ref bits, ReadDimensions(ref bits, geometry));
        private static Vp9PictureGeometry ReadDimensions(ref Vp9BitReader bits, Vp9PictureGeometry geometry) => geometry with { Width = bits.Read(16) + 1, Height = bits.Read(16) + 1 };
        private static Vp9PictureGeometry ReadRender(ref Vp9BitReader bits, Vp9PictureGeometry geometry) => bits.Flag()
            ? geometry with { RenderWidth = bits.Read(16) + 1, RenderHeight = bits.Read(16) + 1 }
            : geometry with { RenderWidth = geometry.Width, RenderHeight = geometry.Height };
    }
}
