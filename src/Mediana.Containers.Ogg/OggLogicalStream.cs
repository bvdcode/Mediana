// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Ogg
{
    internal class OggLogicalStream : IDisposable
    {
        public uint NextSequence { get; set; }
        public long NextPacket { get; set; }
        public bool Ended { get; set; }
        public int PendingBytes { get; set; }
        public long PendingPage { get; set; }
        public int PendingLace { get; set; }
        public List<MediaBuffer> Parts { get; } = [];

        public void Dispose()
        {
            foreach (MediaBuffer part in Parts) { part.Dispose(); }
            Parts.Clear();
            PendingBytes = 0;
        }
    }
}
