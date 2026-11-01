// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text.Json;
using Mediana.Containers.Matroska;
using Mediana.Core.Timing;
using static Mediana.Tests.MatroskaMetadataTestData;

namespace Mediana.Tests
{
    internal static class MatroskaClockTestInput
    {
        public static async Task VerifyReferencesAsync()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "Matroska", "clock-vectors.json");
            using JsonDocument reference = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            int count = 0;
            foreach (JsonElement expected in reference.RootElement.GetProperty("vectors").EnumerateArray())
            {
                ulong bits = ulong.Parse(expected.GetProperty("track_scale_bits").GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                MatroskaTrackClock clock = new(expected.GetProperty("segment_scale").GetUInt64(),
                    new(BitConverter.UInt64BitsToDouble(bits), null, expected.GetProperty("codec_delay").GetUInt64(), 0));
                JsonElement storedOffset = expected.GetProperty("offset");
                MediaDuration? offset = null;
                if (storedOffset.ValueKind != JsonValueKind.Null)
                {
                    offset = new(storedOffset.GetProperty("value").GetInt64(), new(storedOffset.GetProperty("numerator").GetInt32(),
                        storedOffset.GetProperty("denominator").GetInt32()));
                }
                JsonElement timestamp = expected.GetProperty("timestamp");
                if (timestamp.ValueKind == JsonValueKind.Null)
                {
                    await RejectAsync<NotSupportedException>(() => Task.Run(() => clock.GetPresentationTime(
                        expected.GetProperty("cluster").GetUInt64(), expected.GetProperty("relative").GetInt16(), offset)));
                }
                else
                {
                    MediaTimestamp actual = clock.GetPresentationTime(expected.GetProperty("cluster").GetUInt64(),
                        expected.GetProperty("relative").GetInt16(), offset);
                    Require(actual.Value == timestamp.GetInt64() && actual.TimeBase == new TimeBase(1, 1000000000));
                }
                JsonElement duration = expected.GetProperty("duration");
                if (duration.ValueKind == JsonValueKind.Null)
                {
                    await RejectAsync<NotSupportedException>(() => Task.Run(() => clock.GetTrackDuration(expected.GetProperty("duration_ticks").GetUInt64())));
                }
                else
                {
                    MediaDuration actual = clock.GetTrackDuration(expected.GetProperty("duration_ticks").GetUInt64());
                    Require(actual.Value == duration.GetInt64() && actual.TimeBase == MatroskaTrackClock.TimeBase);
                }
                count++;
            }
            Require(count == 1645);
        }

        public static async Task VerifyInvalidAsync()
        {
            foreach (double scale in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                await RejectAsync<ArgumentOutOfRangeException>(() => Task.Run(() => new MatroskaTrackClock(1, new(scale, null, 0, 0))));
            }
            await RejectAsync<ArgumentOutOfRangeException>(() => Task.Run(() => new MatroskaTrackClock(0, new(1, null, 0, 0))));
            MatroskaTrackClock clock = new(1, new(1, null, 0, 0));
            await RejectAsync<ArgumentException>(() => Task.Run(() => clock.GetPresentationTime(0, 0, default(MediaDuration))));
        }

        public static void VerifyAllocation()
        {
            MatroskaTrackClock clock = new(1000000, new(1, null, 513, 0));
            MediaDuration offset = new(1024, new(1, 44100));
            for (int index = 0; index < 512; index++)
            {
                _ = clock.GetPresentationTime((ulong)index, -17, offset);
                _ = clock.GetTrackDuration((ulong)index);
            }
            long sum = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 4096; index++)
            {
                sum += clock.GetPresentationTime((ulong)index, -17, offset).Value;
                sum += clock.GetTrackDuration((ulong)index).Value;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Require(allocated == 0 && sum > 0);
        }
    }
}
