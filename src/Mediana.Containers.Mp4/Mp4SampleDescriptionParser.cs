// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Formats;
using Mediana.Formats.Video;
using Mediana.Formats.Audio;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4SampleDescriptionParser
    {
        public static Mp4SampleDescription Parse(Mp4Box box, MediaKind kind, Mp4ParseBudget budget, bool quickTime)
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
                (MediaKind.Video, "avc1" or "avc3" or "hvc1" or "hev1" or "av01" or "mp4v") => ParseVideo(entry, budget, quickTime),
                (MediaKind.Audio, "mp4a") => ParseAudio(entry, budget),
                (MediaKind.Audio, "ac-3" or "ec-3") => ParseAc3(entry, budget),
                (MediaKind.Data, "tmcd") => ParseTimecode(entry),
                _ => throw new NotSupportedException($"MP4 sample entry '{entry.Type}' is not supported.")
            };
        }

        private static Mp4SampleDescription ParseTimecode(Mp4Box entry)
        {
            Mp4Binary.Range(entry.Payload.Span, 0, 26);
            return new Mp4SampleDescription(entry.Type, "tmcd", StreamFormats.QuickTimeTimecode32, entry.Payload[8..]);
        }

        private static Mp4SampleDescription ParseVideo(Mp4Box entry, Mp4ParseBudget budget, bool quickTime)
        {
            Mp4Binary.Range(entry.Payload.Span, 0, 78);
            int width = Mp4Binary.UInt16(entry.Payload.Span, 24);
            int height = Mp4Binary.UInt16(entry.Payload.Span, 26);
            if (width == 0 || height == 0)
            {
                throw new InvalidDataException("The MP4 visual sample dimensions are invalid.");
            }
            Mp4BoxCollection children = new(entry.Payload[78..], budget, allowTerminator: true);
            RejectEncryption(children);
            (string codecId, EncodedStreamFormat format, ReadOnlyMemory<byte> configuration) = ReadVideoConfiguration(entry.Type, children);
            return new Mp4SampleDescription(entry.Type, codecId, format, configuration, Width: width, Height: height,
                ColorDescriptions: Mp4ColorDescriptionParser.Read(children), AspectRatioDescription: Mp4AspectRatioParser.Read(children, quickTime));
        }

        private static (string CodecId, EncodedStreamFormat Format, ReadOnlyMemory<byte> Configuration) ReadVideoConfiguration(
            string sampleType, Mp4BoxCollection children)
        {
            switch (sampleType)
            {
                case "mp4v":
                    ReadOnlyMemory<byte> visual = Mp4ElementaryStreamDescriptor.ReadVisualConfiguration(children.Required("esds").Payload);
                    return (Mpeg4VisualConfiguration.CodecId, StreamFormats.Mpeg4Visual, visual);
                case "avc1":
                case "avc3":
                    ReadOnlyMemory<byte> avc = children.Required("avcC").Payload;
                    if (avc.Length < 7 || avc.Span[0] != 1)
                    {
                        throw new InvalidDataException("The MP4 AVC configuration record is invalid.");
                    }
                    return ("h264", StreamFormats.Avc, avc);
                case "hvc1":
                case "hev1":
                    ReadOnlyMemory<byte> hevc = children.Required("hvcC").Payload;
                    HevcDecoderConfiguration.Read(hevc);
                    return (HevcDecoderConfiguration.CodecId, StreamFormats.Hevc, hevc);
                case "av01":
                    ReadOnlyMemory<byte> av1 = children.Required("av1C").Payload;
                    Av1CodecConfigurationRecord.Read(av1.Span);
                    return (Av1CodecConfigurationRecord.CodecId, StreamFormats.Av1, av1);
                default:
                    throw new InvalidOperationException("The MP4 video sample entry mapping is inconsistent.");
            }
        }

        private static Mp4SampleDescription ParseAudio(Mp4Box entry, Mp4ParseBudget budget)
        {
            (ushort version, int headerLength, int channels, int rate) = Mp4SoundDescriptionHeader.Read(entry.Payload.Span);
            Mp4BoxCollection children = new(entry.Payload[headerLength..], budget);
            RejectEncryption(children);
            (string codecId, ReadOnlyMemory<byte> configuration) = Mp4ElementaryStreamDescriptor.ReadAudioConfiguration(
                ReadAudioDescriptor(children, version, budget));
            EncodedStreamFormat format = codecId switch
            {
                "aac" => StreamFormats.Aac,
                "mp3" => StreamFormats.Mp3,
                _ => throw new NotSupportedException("Unsupported MP4 audio representation.")
            };
            return new Mp4SampleDescription(entry.Type, codecId, format, configuration,
                AudioChannels: channels, AudioSampleRate: rate);
        }

        private static Mp4SampleDescription ParseAc3(Mp4Box entry, Mp4ParseBudget budget)
        {
            Mp4Binary.Range(entry.Payload.Span, 0, 28);
            if (Mp4Binary.UInt32(entry.Payload.Span, 8) != 0 || Mp4Binary.UInt32(entry.Payload.Span, 12) != 0)
            { throw new InvalidDataException("The MP4 AC-3 sample entry reserved fields must be zero."); }
            Mp4BoxCollection children = new(entry.Payload[28..], budget);
            RejectEncryption(children);
            switch (entry.Type)
            {
                case "ac-3":
                    ReadOnlyMemory<byte> ac3 = children.Required("dac3").Payload;
                    Ac3SpecificConfiguration standard = Ac3SpecificConfiguration.Read(ac3.Span);
                    return new(entry.Type, Ac3FrameHeader.CodecId, StreamFormats.Ac3Mp4, ac3,
                        AudioChannels: standard.ChannelCount, AudioSampleRate: standard.SampleRate);
                case "ec-3":
                    ReadOnlyMemory<byte> eac3 = children.Required("dec3").Payload;
                    Eac3SubstreamConfiguration enhanced = Eac3SpecificConfiguration.Read(eac3.Span).Substreams[0];
                    return new(entry.Type, Ac3FrameHeader.EnhancedCodecId, StreamFormats.Eac3Mp4, eac3,
                        AudioChannels: enhanced.ChannelCount, AudioSampleRate: enhanced.SampleRate);
                default:
                    throw new InvalidOperationException("The MP4 AC-3 sample entry mapping is inconsistent.");
            }
        }

        private static ReadOnlyMemory<byte> ReadAudioDescriptor(Mp4BoxCollection children, ushort version, Mp4ParseBudget budget)
        {
            switch (version)
            {
                case 0:
                    return children.Required("esds").Payload;
                case 1:
                case 2:
                    if (children.Optional("esds") is not null)
                    {
                        throw new InvalidDataException("QuickTime version 1/2 MPEG-4 audio requires its descriptor inside wave.");
                    }
                    Mp4BoxCollection wave = new(children.Required("wave").Payload, budget);
                    RejectEncryption(wave);
                    ReadOnlyMemory<byte> format = wave.Required("frma").Payload;
                    if (format.Length != 4 || !format.Span.SequenceEqual("mp4a"u8))
                    {
                        throw new InvalidDataException("The QuickTime wave format does not match its mp4a sample entry.");
                    }
                    return wave.Required("esds").Payload;
                default:
                    throw new NotSupportedException($"QuickTime sound sample description version {version} is not supported.");
            }
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
