using Mediana.Core;
using Mediana.Core.Timing;
using Mediana.Core.Video;

namespace Mediana.Containers.Mp4
{
    internal static class Mp4MovieParser
    {
        public static Mp4Movie Parse(ReadOnlyMemory<byte> metadata, IReadOnlyList<Mp4DataRange> dataRanges,
            Mp4ReadOptions options, Mp4ParseBudget budget, CancellationToken cancellationToken)
        {
            Mp4BoxCollection movie = new(metadata, budget);
            if (movie.Optional("mvex") is not null)
            {
                throw new NotSupportedException("Fragmented MP4 is not supported.");
            }
            if (movie.Optional("cmov") is not null)
            {
                throw new NotSupportedException("Compressed QuickTime movie headers are not supported.");
            }
            Mp4Box movieHeader = movie.Required("mvhd");
            (TimeBase timeBase, MediaDuration? duration) = ReadTimeHeader(movieHeader, 100, 112);
            VideoDisplayMatrix movieMatrix = Mp4DisplayMatrixParser.Read(movieHeader.Payload.Span,
                movieHeader.Payload.Span[0] == 0 ? 36 : 48);
            List<Mp4TrackInfo> tracks = [];
            List<Mp4SampleTable> tables = [];
            HashSet<uint> trackIds = [];
            foreach (Mp4Box box in movie.Boxes.Where(box => box.Type == "trak"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (tracks.Count == options.MaxTracks)
                {
                    throw new NotSupportedException("The MP4 track count exceeds the configured limit.");
                }
                Mp4BoxCollection track = new(box.Payload, budget);
                ReadOnlyMemory<byte> trackHeader = track.Required("tkhd").Payload;
                byte version = Version(trackHeader.Span, allowFlags: true);
                Mp4Binary.Range(trackHeader.Span, 0, version == 0 ? 84 : 96);
                uint trackId = Mp4Binary.UInt32(trackHeader.Span, version == 0 ? 12 : 20);
                if (trackId == 0 || !trackIds.Add(trackId))
                {
                    throw new InvalidDataException("MP4 track identifiers must be nonzero and unique.");
                }
                MediaDuration? presentationDuration = ReadDuration(trackHeader.Span,
                    version == 0 ? 20 : 28, version, timeBase);
                VideoDisplayMatrix trackMatrix = Mp4DisplayMatrixParser.Read(trackHeader.Span, version == 0 ? 40 : 52);
                Mp4BoxCollection media = new(track.Required("mdia").Payload, budget);
                (TimeBase trackTimeBase, MediaDuration? mediaDuration) = ReadTimeHeader(media.Required("mdhd"), 24, 36);
                ReadOnlyMemory<byte> handler = media.Required("hdlr").Payload;
                Mp4Binary.FullBox(handler.Span);
                Mp4Binary.Range(handler.Span, 0, 24);
                string handlerType = Mp4Binary.FourCc(handler.Span, 8);
                MediaKind kind = handlerType switch
                {
                    "vide" => MediaKind.Video,
                    "soun" => MediaKind.Audio,
                    _ => throw new NotSupportedException($"MP4 track handler '{handlerType}' is not supported.")
                };
                Mp4BoxCollection mediaInfo = new(media.Required("minf").Payload, budget);
                ValidateDataReference(mediaInfo, budget);
                Mp4BoxCollection sampleBoxes = new(mediaInfo.Required("stbl").Payload, budget);
                Mp4SampleDescription description = Mp4SampleDescriptionParser.Parse(sampleBoxes.Required("stsd"), kind, budget);
                if (description.Configuration.Length > options.MaxCodecConfigurationBytes)
                {
                    throw new NotSupportedException("The MP4 codec configuration exceeds the configured limit.");
                }
                Mp4SampleTable table = new(sampleBoxes, options, dataRanges, cancellationToken);
                VideoDisplayMatrix? displayMatrix = kind switch
                {
                    MediaKind.Video => trackMatrix.Then(movieMatrix),
                    MediaKind.Audio => null,
                    _ => throw new InvalidDataException("Unsupported MP4 track kind.")
                };
                EncodedStreamInfo stream = new(tracks.Count, kind, description.CodecId,
                    trackTimeBase, description.Configuration.Span, displayMatrix);
                IReadOnlyList<Mp4Edit> edits = ReadEdits(track, timeBase, options, budget);
                tracks.Add(new Mp4TrackInfo(trackId, stream, description.SampleEntry, mediaDuration,
                    presentationDuration, edits, description.Width, description.Height,
                    description.AudioChannels, description.AudioSampleRate, table.SampleCount, trackMatrix));
                tables.Add(table);
            }
            if (tracks.Count == 0)
            {
                throw new InvalidDataException("The MP4 movie has no tracks.");
            }
            return new Mp4Movie(timeBase, duration, movieMatrix, tracks.ToArray(), tables.ToArray());
        }

        private static (TimeBase, MediaDuration?) ReadTimeHeader(Mp4Box box, int minimumV0, int minimumV1)
        {
            byte version = Version(box.Payload.Span);
            Mp4Binary.Range(box.Payload.Span, 0, version == 0 ? minimumV0 : minimumV1);
            uint scale = Mp4Binary.UInt32(box.Payload.Span, version == 0 ? 12 : 20);
            if (scale == 0)
            {
                throw new InvalidDataException($"The MP4 '{box.Type}' time scale is zero.");
            }
            if (scale > int.MaxValue)
            {
                throw new NotSupportedException("MP4 time scales above the signed 32-bit range are not supported.");
            }
            TimeBase timeBase = new(1, (int)scale);
            return (timeBase, ReadDuration(box.Payload.Span, version == 0 ? 16 : 24, version, timeBase));
        }

        private static MediaDuration? ReadDuration(ReadOnlySpan<byte> data, int offset, byte version, TimeBase timeBase)
        {
            ulong value = version == 0 ? Mp4Binary.UInt32(data, offset) : Mp4Binary.UInt64(data, offset);
            if (value == (version == 0 ? uint.MaxValue : ulong.MaxValue))
            {
                return null;
            }
            return new MediaDuration(Mp4Binary.Offset64(value), timeBase);
        }

        private static byte Version(ReadOnlySpan<byte> data, bool allowFlags = false)
        {
            byte version = Mp4Binary.Range(data, 0, 1)[0];
            if (version > 1)
            {
                throw new NotSupportedException("The MP4 header version is not supported.");
            }
            Mp4Binary.FullBox(data, version, allowFlags);
            return version;
        }

        private static void ValidateDataReference(Mp4BoxCollection mediaInfo, Mp4ParseBudget budget)
        {
            Mp4BoxCollection information = new(mediaInfo.Required("dinf").Payload, budget);
            ReadOnlyMemory<byte> reference = information.Required("dref").Payload;
            Mp4Binary.FullBox(reference.Span);
            if (Mp4Binary.UInt32(reference.Span, 4) != 1)
            {
                throw new NotSupportedException("MP4 requires one self-contained data reference.");
            }
            Mp4BoxCollection entries = new(reference[8..], budget);
            if (entries.Boxes.Count != 1 || entries.Boxes[0].Type != "url " ||
                Mp4Binary.UInt32(entries.Boxes[0].Payload.Span, 0) != 1)
            {
                throw new NotSupportedException("External MP4 data references are not supported.");
            }
        }

        private static IReadOnlyList<Mp4Edit> ReadEdits(Mp4BoxCollection track, TimeBase movieTimeBase,
            Mp4ReadOptions options, Mp4ParseBudget budget)
        {
            Mp4Box? editBox = track.Optional("edts");
            if (editBox is null)
            {
                return Array.Empty<Mp4Edit>();
            }
            Mp4BoxCollection children = new(editBox.Value.Payload, budget);
            ReadOnlyMemory<byte> payload = children.Required("elst").Payload;
            byte version = Version(payload.Span);
            Mp4Table table = new(payload, version == 0 ? 12 : 20, version);
            if (table.Count > options.MaxEditsPerTrack)
            {
                throw new NotSupportedException("The MP4 edit count exceeds the configured limit.");
            }
            Mp4Edit[] edits = new Mp4Edit[table.Count];
            for (int index = 0; index < edits.Length; index++)
            {
                ulong duration = version == 0 ? table.UInt32(index) : table.UInt64(index);
                long mediaTime = version == 0 ? table.Int32(index, 4) : unchecked((long)table.UInt64(index, 8));
                if (mediaTime < -1)
                {
                    throw new InvalidDataException("An MP4 edit media time is invalid.");
                }
                int rate = table.Int32(index, version == 0 ? 8 : 16);
                edits[index] = new Mp4Edit(new MediaDuration(Mp4Binary.Offset64(duration), movieTimeBase), mediaTime, rate);
            }
            return Array.AsReadOnly(edits);
        }
    }
}
