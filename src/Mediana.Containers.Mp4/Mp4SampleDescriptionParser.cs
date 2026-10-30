// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4SampleDescriptionParser
    {
        public static Mp4SampleDescription Parse(Mp4Box box, MediaKind kind, Mp4ParseBudget budget)
        {
            Mp4Binary.FullBox(box.Payload.Span);
            uint count = Mp4Binary.UInt32(box.Payload.Span, 4);
            if (count != 1)
            {
                throw new NotSupportedException("MP4 requires one sample description per track.");
            }
            Mp4BoxCollection entries = new(box.Payload[8..], budget);
            if (entries.Boxes.Count != 1)
            {
                throw new InvalidDataException("The MP4 sample description count is inconsistent.");
            }
            Mp4Box entry = entries.Boxes[0];
            if (Mp4Binary.UInt16(entry.Payload.Span, 6) != 1)
            {
                throw new NotSupportedException("MP4 requires the first self-contained data reference.");
            }
            return (kind, entry.Type) switch
            {
                (MediaKind.Video, "avc1" or "avc3") => ParseVideo(entry, budget),
                (MediaKind.Audio, "mp4a") => ParseAudio(entry, budget),
                _ => throw new NotSupportedException($"MP4 sample entry '{entry.Type}' is not supported.")
            };
        }

        private static Mp4SampleDescription ParseVideo(Mp4Box entry, Mp4ParseBudget budget)
        {
            Mp4Binary.Range(entry.Payload.Span, 0, 78);
            int width = Mp4Binary.UInt16(entry.Payload.Span, 24);
            int height = Mp4Binary.UInt16(entry.Payload.Span, 26);
            if (width == 0 || height == 0)
            {
                throw new InvalidDataException("The MP4 visual sample dimensions are invalid.");
            }
            Mp4BoxCollection children = new(entry.Payload[78..], budget);
            RejectEncryption(children);
            ReadOnlyMemory<byte> configuration = children.Required("avcC").Payload;
            if (configuration.Length < 7 || configuration.Span[0] != 1)
            {
                throw new InvalidDataException("The MP4 AVC configuration record is invalid.");
            }
            return new Mp4SampleDescription(entry.Type, "h264", configuration, Width: width, Height: height);
        }

        private static Mp4SampleDescription ParseAudio(Mp4Box entry, Mp4ParseBudget budget)
        {
            Mp4Binary.Range(entry.Payload.Span, 0, 28);
            if (Mp4Binary.UInt16(entry.Payload.Span, 8) != 0)
            {
                throw new NotSupportedException("Versioned QuickTime sound sample entries are not supported.");
            }
            int channels = Mp4Binary.UInt16(entry.Payload.Span, 16);
            uint rate = Mp4Binary.UInt32(entry.Payload.Span, 24);
            if (channels == 0 || rate == 0 || (rate & 0xFFFF) != 0)
            {
                throw new NotSupportedException("The MP4 audio sample declaration is not supported.");
            }
            Mp4BoxCollection children = new(entry.Payload[28..], budget);
            RejectEncryption(children);
            (string codecId, ReadOnlyMemory<byte> configuration) = Mp4ElementaryStreamDescriptor.ReadAudioConfiguration(
                children.Required("esds").Payload);
            return new Mp4SampleDescription(entry.Type, codecId, configuration,
                AudioChannels: channels, AudioSampleRate: (int)(rate >> 16));
        }

        private static void RejectEncryption(Mp4BoxCollection children)
        {
            if (children.Optional("sinf") is not null)
            {
                throw new NotSupportedException("Encrypted MP4 sample entries are not supported.");
            }
        }
    }
}
