// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.Av1
{
    internal class Av1SequenceTiming : IEquatable<Av1SequenceTiming>
    {
        private readonly Av1OperatingPoint[] operatingPoints;

        private Av1SequenceTiming(Av1TimingInfo info, Av1DecoderModelInfo? decoderModel, ReadOnlySpan<Av1OperatingPoint> points)
        {
            Info = info;
            DecoderModel = decoderModel;
            operatingPoints = points.ToArray();
        }

        internal Av1TimingInfo Info { get; }
        internal Av1DecoderModelInfo? DecoderModel { get; }
        internal ReadOnlySpan<Av1OperatingPoint> OperatingPoints => operatingPoints;

        internal uint? ReadPresentationTime(ref Av1BitReader bits)
        {
            if (DecoderModel is Av1DecoderModelInfo model && !Info.EqualPictureInterval)
            {
                return bits.ReadUnsigned(model.FramePresentationTimeLength);
            }
            return null;
        }

        internal uint? ReadBufferRemovalTime(ref Av1BitReader bits)
        {
            if (DecoderModel is not Av1DecoderModelInfo model || !bits.Flag()) { return null; }
            uint? selected = null;
            for (int index = 0; index < operatingPoints.Length; index++)
            {
                Av1OperatingPoint point = operatingPoints[index];
                if (point.DecoderBufferDelay.HasValue && point.IncludesBaseLayer)
                {
                    uint value = bits.ReadUnsigned(model.BufferRemovalTimeLength);
                    if (index == 0) { selected = value; }
                }
            }
            return selected;
        }

        internal static Av1SequenceTiming? Read(ref Av1BitReader bits, out int level, out bool tier)
        {
            Av1TimingInfo? timing = null;
            Av1DecoderModelInfo? model = null;
            if (bits.Flag())
            {
                timing = Av1TimingInfo.Read(ref bits);
                if (bits.Flag()) { model = Av1DecoderModelInfo.Read(ref bits); }
            }
            bool initialDelay = bits.Flag();
            int count = bits.Read(5) + 1;
            Span<Av1OperatingPoint> points = stackalloc Av1OperatingPoint[32];
            for (int index = 0; index < count; index++)
            {
                int idc = bits.Read(12);
                int pointLevel = bits.Read(5);
                bool pointTier = pointLevel > 7 && bits.Flag();
                uint? decoderDelay = null;
                uint? encoderDelay = null;
                bool lowDelay = false;
                if (model is Av1DecoderModelInfo decoder && bits.Flag())
                {
                    decoderDelay = bits.ReadUnsigned(decoder.BufferDelayLength);
                    encoderDelay = bits.ReadUnsigned(decoder.BufferDelayLength);
                    lowDelay = bits.Flag();
                }
                int? displayDelay = initialDelay && bits.Flag() ? bits.Read(4) + 1 : null;
                points[index] = new(idc, pointLevel, pointTier, decoderDelay, encoderDelay, lowDelay, displayDelay);
            }
            if (!points[0].IncludesBaseLayer)
            {
                throw new NotSupportedException("The first AV1 operating point does not include the supported base layer.");
            }
            level = points[0].Level;
            tier = points[0].Tier;
            return timing is Av1TimingInfo info ? new(info, model, points[..count]) : null;
        }

        public bool Equals(Av1SequenceTiming? other) => other is not null && Info == other.Info &&
            DecoderModel == other.DecoderModel && OperatingPoints.SequenceEqual(other.OperatingPoints);

        public override bool Equals(object? obj) => obj is Av1SequenceTiming other && Equals(other);

        public override int GetHashCode()
        {
            HashCode hash = new();
            hash.Add(Info);
            hash.Add(DecoderModel);
            foreach (Av1OperatingPoint point in operatingPoints) { hash.Add(point); }
            return hash.ToHashCode();
        }
    }
}
