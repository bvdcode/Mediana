// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal static partial class Av1CoefficientCdfs
    {
        internal static void Initialize(Span<ushort> target, int quantizer)
        {
            int group = quantizer switch { <= 20 => 0, <= 60 => 1, <= 120 => 2, _ => 3 };
            ReadOnlySpan<ushort> source = group switch { 0 => Values0, 1 => Values1, 2 => Values2, 3 => Values3, _ => throw new InvalidOperationException("The AV1 quantizer context is invalid.") };
            source.Slice(0, 195).CopyTo(target.Slice(Av1DefaultCdfs.AllZeroOffset, 195));
            source.Slice(195, 24).CopyTo(target.Slice(Av1DefaultCdfs.Eob16Offset, 24));
            source.Slice(219, 270).CopyTo(target.Slice(Av1DefaultCdfs.EobExtraOffset, 270));
            source.Slice(489, 18).CopyTo(target.Slice(Av1DefaultCdfs.DcSignOffset, 18));
            source.Slice(507, 160).CopyTo(target.Slice(Av1DefaultCdfs.CoeffBaseEobOffset, 160));
            source.Slice(667, 2100).CopyTo(target.Slice(Av1DefaultCdfs.CoeffBaseOffset, 2100));
            source.Slice(2767, 1050).CopyTo(target.Slice(Av1DefaultCdfs.CoeffBrOffset, 1050));
            source.Slice(3817, 28).CopyTo(target.Slice(Av1DefaultCdfs.Eob32Offset, 28));
            source.Slice(3845, 32).CopyTo(target.Slice(Av1DefaultCdfs.Eob64Offset, 32));
            source.Slice(3877, 36).CopyTo(target.Slice(Av1DefaultCdfs.Eob128Offset, 36));
            source.Slice(3913, 40).CopyTo(target.Slice(Av1DefaultCdfs.Eob256Offset, 40));
            source.Slice(3953, 22).CopyTo(target.Slice(Av1DefaultCdfs.Eob512Offset, 22));
            source.Slice(3975, 24).CopyTo(target.Slice(Av1DefaultCdfs.Eob1024Offset, 24));
        }

    }
}
