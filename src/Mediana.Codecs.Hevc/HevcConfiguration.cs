// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Collections.ObjectModel;
using Mediana.Formats.Video;

namespace Mediana.Codecs.Hevc
{
    internal class HevcConfiguration
    {
        private readonly IReadOnlyDictionary<int, ReadOnlyMemory<byte>> rawPictures;
        private readonly IReadOnlyDictionary<int, int> sequenceLengths;

        private HevcConfiguration(int lengthSize, Dictionary<int, SequenceParameters> sequences,
            Dictionary<int, ReadOnlyMemory<byte>> rawPictures, Dictionary<int, int> sequenceLengths, int maximumBytes)
        {
            LengthSize = lengthSize;
            Sequences = new ReadOnlyDictionary<int, SequenceParameters>(sequences);
            this.rawPictures = new ReadOnlyDictionary<int, ReadOnlyMemory<byte>>(rawPictures);
            this.sequenceLengths = new ReadOnlyDictionary<int, int>(sequenceLengths);
            long totalBytes = sequenceLengths.Values.Sum(length => (long)length) + rawPictures.Values.Sum(value => (long)value.Length);
            if (totalBytes > maximumBytes) { throw new NotSupportedException("HEVC parameter sets exceed their configured byte limit."); }
            Dictionary<int, PictureParameters> pictures = [];
            foreach (ReadOnlyMemory<byte> bytes in rawPictures.Values)
            {
                PictureParameters picture = PictureParameters.Read(new(bytes), Sequences);
                pictures.Add(picture.Id, picture);
            }
            Pictures = new ReadOnlyDictionary<int, PictureParameters>(pictures);
        }

        public int LengthSize { get; }
        public IReadOnlyDictionary<int, SequenceParameters> Sequences { get; }
        public IReadOnlyDictionary<int, PictureParameters> Pictures { get; }

        public static HevcConfiguration Read(ReadOnlyMemory<byte> data, HevcDecoderOptions options)
        {
            options.Validate();
            if (data.Length > options.MaxConfigurationBytes) { throw new NotSupportedException("HEVC configuration exceeds its configured byte limit."); }
            HevcDecoderConfiguration configuration = HevcDecoderConfiguration.Read(data);
            Dictionary<int, SequenceParameters> sequences = [];
            Dictionary<int, int> sequenceLengths = [];
            Dictionary<int, ReadOnlyMemory<byte>> rawPictures = [];
            foreach (HevcNalArray array in configuration.Arrays)
            {
                if (array.Type != 33) { continue; }
                foreach (ReadOnlyMemory<byte> bytes in array.Units)
                {
                    SequenceParameters sequence = SequenceParameters.Read(new(bytes), options.MaxPicturePixels);
                    if (!sequences.TryAdd(sequence.Id, sequence)) { throw new InvalidDataException("Duplicate HEVC SPS identifier."); }
                    sequenceLengths.Add(sequence.Id, bytes.Length);
                }
            }
            foreach (HevcNalArray array in configuration.Arrays)
            {
                if (array.Type != 34) { continue; }
                foreach (ReadOnlyMemory<byte> bytes in array.Units)
                {
                    PictureParameters picture = PictureParameters.Read(new(bytes), sequences);
                    if (!rawPictures.TryAdd(picture.Id, bytes.ToArray())) { throw new InvalidDataException("Duplicate HEVC PPS identifier."); }
                }
            }
            return new(configuration.LengthSize, sequences, rawPictures, sequenceLengths, options.MaxConfigurationBytes);
        }

        public HevcConfiguration WithParameter(NalPayload nal, HevcDecoderOptions options)
        {
            Dictionary<int, SequenceParameters> sequences = new(Sequences);
            Dictionary<int, ReadOnlyMemory<byte>> updated = new(rawPictures);
            Dictionary<int, int> lengths = new(sequenceLengths);
            switch (nal.Type)
            {
                case 33:
                    SequenceParameters sequence = SequenceParameters.Read(nal, options.MaxPicturePixels);
                    sequences[sequence.Id] = sequence; lengths[sequence.Id] = nal.Encoded.Length; break;
                case 34:
                    PictureParameters picture = PictureParameters.Read(nal, sequences);
                    updated[picture.Id] = nal.Encoded.ToArray(); break;
                default: throw new InvalidDataException("Expected an HEVC SPS or PPS.");
            }
            return new(LengthSize, sequences, updated, lengths, options.MaxConfigurationBytes);
        }

        public string? GetUnsupportedReason()
        {
            if (Sequences.Count == 0 || Pictures.Count == 0) { return "HEVC video requires sequence and picture parameter sets."; }
            foreach (SequenceParameters sequence in Sequences.Values)
            {
                if (sequence.ChromaFormat != 1) { return "HEVC video output requires YUV 4:2:0 samples."; }
                if (sequence.Video.ChromaLocation != sequence.Video.BottomChromaLocation)
                { return "Different top and bottom chroma locations are not represented in progressive HEVC video output."; }
            }
            return null;
        }
    }
}
