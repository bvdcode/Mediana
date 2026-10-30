// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Codecs.H264
{
    internal class H264Configuration(int lengthSize,
        IReadOnlyDictionary<int, H264SequenceParameterSet> sequences,
        IReadOnlyDictionary<int, H264PictureParameterSet> pictures)
    {
        public int LengthSize { get; } = lengthSize;
        public IReadOnlyDictionary<int, H264SequenceParameterSet> Sequences { get; } = sequences;
        public IReadOnlyDictionary<int, H264PictureParameterSet> Pictures { get; } = pictures;
        private IReadOnlyDictionary<int, int> SequenceLengths { get; init; } = new Dictionary<int, int>();
        private IReadOnlyDictionary<int, int> PictureLengths { get; init; } = new Dictionary<int, int>();
        private int ConfigurationBytes { get; init; } = 7;

        public H264Configuration WithParameterSet(ReadOnlySpan<byte> nal, H264DecoderOptions options)
        {
            options.Validate();
            if (nal.Length > options.MaxParameterSetBytes)
            {
                throw new NotSupportedException("The AVC parameter set exceeds the configured byte limit.");
            }
            int type = H264Nal.Type(nal);
            if (type == 7)
            {
                H264SequenceParameterSet sequence = H264SequenceParser.Parse(nal, options);
                int bytes = UpdatedBytes(nal.Length, SequenceLengths, sequence.Id, options);
                Dictionary<int, H264SequenceParameterSet> updated = new(Sequences) { [sequence.Id] = sequence };
                Dictionary<int, int> lengths = new(SequenceLengths) { [sequence.Id] = nal.Length + 2 };
                return new H264Configuration(LengthSize, updated, Pictures)
                {
                    SequenceLengths = lengths,
                    PictureLengths = PictureLengths,
                    ConfigurationBytes = bytes
                };
            }
            if (type == 8)
            {
                H264PictureParameterSet picture = H264PictureParser.Parse(nal, Sequences);
                int bytes = UpdatedBytes(nal.Length, PictureLengths, picture.Id, options);
                Dictionary<int, H264PictureParameterSet> updated = new(Pictures) { [picture.Id] = picture };
                Dictionary<int, int> lengths = new(PictureLengths) { [picture.Id] = nal.Length + 2 };
                return new H264Configuration(LengthSize, Sequences, updated)
                {
                    SequenceLengths = SequenceLengths,
                    PictureLengths = lengths,
                    ConfigurationBytes = bytes
                };
            }
            throw new InvalidDataException("Expected an H.264 sequence or picture parameter set.");
        }

        private int UpdatedBytes(int length, IReadOnlyDictionary<int, int> lengths, int id, H264DecoderOptions options)
        {
            lengths.TryGetValue(id, out int previous);
            long total = (long)ConfigurationBytes - previous + length + 2;
            if (total > options.MaxConfigurationBytes)
            {
                throw new NotSupportedException("The AVC configuration exceeds the configured byte limit.");
            }
            return (int)total;
        }

        public static H264Configuration Parse(ReadOnlySpan<byte> data, H264DecoderOptions options)
        {
            options.Validate();
            H264ConfigurationRecord record = new(data, options.MaxConfigurationBytes, options.MaxParameterSetBytes);
            Dictionary<int, H264SequenceParameterSet> sequences = [];
            Dictionary<int, int> sequenceLengths = [];
            for (int index = 0; index < record.SequenceCount; index++)
            {
                ReadOnlySpan<byte> nal = record.ReadNal();
                H264SequenceParameterSet sequence = H264SequenceParser.Parse(nal, options);
                if (!sequences.TryAdd(sequence.Id, sequence))
                {
                    throw new InvalidDataException("The AVC sequence parameter identifier is duplicated.");
                }
                sequenceLengths.Add(sequence.Id, nal.Length + 2);
            }
            int pictureCount = record.ReadPictureCount();
            Dictionary<int, H264PictureParameterSet> pictures = [];
            Dictionary<int, int> pictureLengths = [];
            for (int index = 0; index < pictureCount; index++)
            {
                ReadOnlySpan<byte> nal = record.ReadNal();
                H264PictureParameterSet picture = H264PictureParser.Parse(nal, sequences);
                if (!pictures.TryAdd(picture.Id, picture))
                {
                    throw new InvalidDataException("The AVC picture parameter identifier is duplicated.");
                }
                pictureLengths.Add(picture.Id, nal.Length + 2);
            }
            record.Finish();
            if (sequences.Count == 0 || pictures.Count == 0)
            {
                throw new InvalidDataException("The AVC configuration is incomplete.");
            }
            return new H264Configuration(record.LengthSize, sequences, pictures)
            {
                SequenceLengths = sequenceLengths,
                PictureLengths = pictureLengths,
                ConfigurationBytes = data.Length
            };
        }

    }
}
