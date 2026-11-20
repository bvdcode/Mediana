// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Containers.Mp4
{
    internal static class Mp4FragmentIndexParser
    {
        public static void Apply(Mp4Movie movie, Mp4Box? extends, IReadOnlyList<Mp4MovieFragment> fragments,
            IReadOnlyList<Mp4DataRange> ranges, Mp4ReadOptions options, Mp4ParseBudget budget, CancellationToken token)
        {
            if (extends is null)
            {
                if (fragments.Count != 0)
                {
                    throw new InvalidDataException("MP4 movie fragments require a movie extends box.");
                }
                return;
            }
            Dictionary<uint, int> trackIndices = movie.Tracks.ToDictionary(track => track.TrackId, track => track.Stream.StreamIndex);
            Dictionary<uint, Mp4FragmentDefaults> defaults = ReadDefaults(extends.Value.Payload, trackIndices, budget);
            Mp4FragmentSampleIndex[] indices = movie.Tables.Select(table => new Mp4FragmentSampleIndex(table)).ToArray();
            Mp4FragmentRecoveryIndex[] recovery = movie.Tracks.Select(track => new Mp4FragmentRecoveryIndex(track.SampleCount, track.RecoveryGroups)).ToArray();
            uint? previousSequence = null;
            foreach (Mp4MovieFragment fragment in fragments)
            {
                token.ThrowIfCancellationRequested();
                Mp4BoxCollection boxes = new(fragment.Payload, budget);
                ReadOnlySpan<byte> header = boxes.Required("mfhd").Payload.Span;
                Mp4Binary.FullBox(header);
                if (header.Length != 8)
                {
                    throw new InvalidDataException("The MP4 movie fragment header length is invalid.");
                }
                uint sequence = Mp4Binary.UInt32(header, 4);
                if (previousSequence is uint previous && sequence <= previous)
                {
                    throw new InvalidDataException("MP4 movie fragment sequence numbers must increase.");
                }
                previousSequence = sequence;
                long implicitOffset = fragment.Offset;
                foreach (Mp4Box box in boxes.Boxes.Where(box => box.Type == "traf"))
                {
                    token.ThrowIfCancellationRequested();
                    Mp4BoxCollection track = new(box.Payload, budget);
                    ReadOnlySpan<byte> trackHeader = track.Required("tfhd").Payload.Span;
                    uint trackId = Mp4Binary.UInt32(trackHeader, 4);
                    if (!trackIndices.TryGetValue(trackId, out int index))
                    {
                        throw new InvalidDataException("An MP4 track fragment references an unknown track.");
                    }
                    Mp4TrackFragmentHeader configuration = Mp4TrackFragmentHeader.Read(trackHeader, defaults[trackId], fragment.Offset, implicitOffset);
                    Mp4FragmentSampleIndex samples = indices[index];
                    Mp4Box? decodeTime = track.Optional("tfdt");
                    if (decodeTime is not null)
                    {
                        samples.StartFragment(ReadDecodeTime(decodeTime.Value.Payload.Span), token);
                    }
                    int firstSample = samples.SampleCount;
                    long nextOffset = configuration.BaseOffset;
                    bool hasRuns = false;
                    foreach (Mp4Box runBox in track.Boxes.Where(child => child.Type == "trun"))
                    {
                        if (configuration.DurationIsEmpty)
                        {
                            throw new InvalidDataException("An empty-duration MP4 fragment cannot contain track runs.");
                        }
                        hasRuns = true;
                        Mp4FragmentRun run = new(runBox.Payload, samples.SampleCount, samples.EndDecodeTime,
                            configuration.BaseOffset, nextOffset, configuration.Defaults, options.MaxSamplesPerTrack - samples.SampleCount, ranges, token);
                        samples.Add(run, token);
                        nextOffset = run.EndOffset;
                    }
                    if (configuration.DurationIsEmpty)
                    {
                        if (movie.Tracks[index].Edits.Count != 0)
                        {
                            throw new InvalidDataException("MP4 empty-duration fragments cannot be combined with edit lists.");
                        }
                        samples.AddEmptyDuration(configuration.Defaults.Duration);
                    }
                    int count = samples.SampleCount - firstSample;
                    recovery[index].Add(count, Mp4RecoveryGroupParser.Parse(track, count, options, token, movie.Tracks[index].RecoveryGroups));
                    if (hasRuns)
                    {
                        implicitOffset = nextOffset;
                    }
                }
            }
            for (int index = 0; index < indices.Length; index++)
            {
                movie.Tables[index] = indices[index];
                movie.Tracks[index] = movie.Tracks[index].WithIndex(indices[index], recovery[index].Build());
            }
        }

        private static Dictionary<uint, Mp4FragmentDefaults> ReadDefaults(ReadOnlyMemory<byte> payload,
            IReadOnlyDictionary<uint, int> tracks, Mp4ParseBudget budget)
        {
            Mp4BoxCollection boxes = new(payload, budget);
            Dictionary<uint, Mp4FragmentDefaults> defaults = [];
            foreach (Mp4Box box in boxes.Boxes.Where(box => box.Type == "trex"))
            {
                ReadOnlySpan<byte> data = box.Payload.Span;
                Mp4Binary.FullBox(data);
                if (data.Length != 24)
                {
                    throw new InvalidDataException("The MP4 track extends header length is invalid.");
                }
                uint trackId = Mp4Binary.UInt32(data, 4);
                Mp4FragmentDefaults values = new(Mp4Binary.UInt32(data, 8), Mp4Binary.UInt32(data, 12),
                    Mp4Binary.UInt32(data, 16), Mp4Binary.UInt32(data, 20));
                if (!tracks.ContainsKey(trackId) || !defaults.TryAdd(trackId, values))
                {
                    throw new InvalidDataException("MP4 track extends identifiers must be known and unique.");
                }
            }
            if (defaults.Count != tracks.Count)
            {
                throw new InvalidDataException("MP4 movie extends must declare defaults for every track.");
            }
            return defaults;
        }

        private static long ReadDecodeTime(ReadOnlySpan<byte> data)
        {
            byte version = Mp4Binary.Range(data, 0, 1)[0];
            if (version > 1)
            {
                throw new NotSupportedException("The MP4 track fragment decode time version is unsupported.");
            }
            Mp4Binary.FullBox(data, version);
            if (data.Length != (version == 1 ? 12 : 8))
            {
                throw new InvalidDataException("The MP4 track fragment decode time length is invalid.");
            }
            return version == 1 ? Mp4Binary.Offset64(Mp4Binary.UInt64(data, 4)) : Mp4Binary.UInt32(data, 4);
        }
    }
}
