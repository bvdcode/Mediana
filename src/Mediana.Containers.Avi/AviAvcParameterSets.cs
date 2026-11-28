// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Formats.Video;

namespace Mediana.Containers.Avi
{
    internal class AviAvcParameterSets
    {
        private const int AviNalLengthBytes = 4;
        private static H264HeaderLimits Limits { get; } = H264HeaderLimits.Default with { MaxPicturePixels = null };
        private readonly Dictionary<int, byte[]> sequences = [];
        private readonly Dictionary<int, byte[]> pictures = [];
        private H264Configuration? opening;
        private H264Configuration? restart;

        public AviAvcParameterSets(ReadOnlyMemory<byte> extra)
        {
            if (extra.Length > Limits.MaxConfigurationBytes)
            {
                throw new NotSupportedException("The AVI AVC configuration exceeds the configured byte limit.");
            }
            Configuration = H264Configuration.EmptyAnnexB;
            if (extra.IsEmpty) { return; }
            if (extra.Span[0] == 1)
            {
                H264ConfigurationRecord record = new(extra.Span, Limits.MaxConfigurationBytes, Limits.MaxParameterSetBytes);
                LengthSize = record.LengthSize;
                for (int index = 0; index < record.SequenceCount; index++) { Add(record.ReadNal()); }
                int pictureCount = record.ReadPictureCount();
                for (int index = 0; index < pictureCount; index++) { Add(record.ReadNal()); }
                record.Finish();
            }
            else
            {
                LengthSize = H264NalReader.StartsWithAnnexB(extra.Span) ? 0 : AviNalLengthBytes;
                H264NalReader units = new(extra, LengthSize, H264HeaderLimits.DefaultMaxNalsPerPacket);
                while (units.TryRead(out ReadOnlyMemory<byte> nal)) { Add(nal.Span); }
            }
        }

        public int LengthSize { get; private set; }
        public H264Configuration Configuration { get; private set; }

        public void SelectPacketFraming(ReadOnlySpan<byte> prefix, int packetLength)
        {
            if (LengthSize == 0 || prefix.Length <= LengthSize || !H264NalReader.StartsWithAnnexB(prefix))
            {
                return;
            }
            uint length = H264NalReader.ReadLength(prefix, LengthSize);
            if (length == 0 || length > packetLength - LengthSize ||
                length == 1 && (prefix[LengthSize] & 31) is 1 or 5 or 6 or 7 or 8 or 9)
            {
                LengthSize = 0;
            }
        }

        public void BeginPacket() => restart = opening;

        public void Add(ReadOnlySpan<byte> nal)
        {
            int type = H264Nal.Type(nal);
            if (type is not (7 or 8))
            {
                throw new InvalidDataException("The AVI AVC configuration contains a NAL unit other than SPS or PPS.");
            }
            H264Configuration previous = Configuration;
            Configuration = previous.WithParameterSet(nal, Limits);
            if (opening is null)
            {
                H264BitReader bits = H264Nal.Payload(nal, type);
                if (type == 7) { bits.ReadBits(24); }
                int id = bits.ReadUe(type == 7 ? 31 : 255);
                Dictionary<int, byte[]> sets = type == 7 ? sequences : pictures;
                sets[id] = nal.ToArray();
            }
            else if (ReferenceEquals(restart, previous))
            {
                restart = Configuration;
            }
            else if (restart is not null && (type == 7 || CanReadPicture(restart, nal)))
            {
                restart = restart.WithParameterSet(nal, Limits);
            }
            else
            {
                restart = null;
            }
        }

        public byte[] SaveOpening()
        {
            opening = restart = Configuration;
            return H264ConfigurationRecord.Write(LengthSize == 0 ? AviNalLengthBytes : LengthSize,
                sequences.Values, pictures.Values);
        }

        public bool CanRestart(H264SliceHeader picture) => restart is not null &&
            restart.Pictures.TryGetValue(picture.Picture.Id, out H264PictureParameterSet? parameters) &&
            parameters == picture.Picture &&
            restart.Sequences.TryGetValue(parameters.SequenceId, out H264SequenceParameterSet? sequence) &&
            sequence == picture.Sequence;

        private static bool CanReadPicture(H264Configuration configuration, ReadOnlySpan<byte> nal)
        {
            H264BitReader bits = H264Nal.Payload(nal, 8);
            bits.ReadUe(255);
            return configuration.Sequences.ContainsKey(bits.ReadUe(31));
        }
    }
}
