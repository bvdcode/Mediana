// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;

namespace Mediana.Containers.Mp4
{
    /// <summary>Original unsigned header dates in whole seconds since 1904-01-01.
    /// Zero is retained but omitted from calendar projections as an unspecified-value convention.
    /// Out-of-range dates remain available as numbers. UTC is used only for ISO declarations.</summary>
    public class Mp4HeaderDates
    {
        private static readonly long EpochTicks = new DateTime(1904, 1, 1).Ticks;
        private static readonly ulong MaxSeconds = (ulong)((DateTime.MaxValue.Ticks - EpochTicks) / TimeSpan.TicksPerSecond);

        public Mp4HeaderDates(ulong creationSeconds, ulong modificationSeconds, DateTimeKind kind)
        {
            switch (kind)
            {
                case DateTimeKind.Utc:
                case DateTimeKind.Unspecified:
                    break;
                case DateTimeKind.Local:
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
            CreationSeconds = creationSeconds;
            ModificationSeconds = modificationSeconds;
            Kind = kind;
        }

        public ulong CreationSeconds { get; }
        public ulong ModificationSeconds { get; }
        public DateTimeKind Kind { get; }
        public DateTime? CreationTime => ToCalendarTime(CreationSeconds);
        public DateTime? ModificationTime => ToCalendarTime(ModificationSeconds);

        private DateTime? ToCalendarTime(ulong seconds)
        {
            if (seconds == 0 || seconds > MaxSeconds)
            {
                return null;
            }
            return new DateTime(EpochTicks + (long)seconds * TimeSpan.TicksPerSecond, Kind);
        }

        internal IEnumerable<MediaDateTimeTag> GetTags(string keyNamespace, string sourcePath)
        {
            if (CreationTime is DateTime created)
            {
                yield return new(keyNamespace, "creation_time", created, sourcePath);
            }
            if (ModificationTime is DateTime modified)
            {
                yield return new(keyNamespace, "modification_time", modified, sourcePath);
            }
        }
    }
}
