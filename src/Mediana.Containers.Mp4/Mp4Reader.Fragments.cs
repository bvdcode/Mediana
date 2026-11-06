// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;

namespace Mediana.Containers.Mp4
{
    public partial class Mp4Reader
    {
        internal MediaBuffer CreateFragmentInitialization(IReadOnlyList<int> streamIndices,
            MediaBufferAllocator outputAllocator, CancellationToken token)
        {
            Enter();
            try
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (streamIndices.Count == 0 || streamIndices.Distinct().Count() != streamIndices.Count)
                {
                    throw new ArgumentException("Initialization requires distinct nonempty stream selection.", nameof(streamIndices));
                }
                foreach (int stream in streamIndices) { ValidateIndexedStream(stream); }
                return Mp4FragmentInitialization.Create(metadata.Memory,
                    streamIndices.Select(index => Tracks[index]).ToArray(), outputAllocator, token);
            }
            finally { Volatile.Write(ref busy, 0); }
        }

        internal Mp4SampleLocation GetFragmentSample(int streamIndex, int sampleIndex, CancellationToken token)
        {
            Enter();
            try
            {
                ValidateIndexedStream(streamIndex);
                return Locate(streamIndex, sampleIndex, token);
            }
            finally { Volatile.Write(ref busy, 0); }
        }
    }
}
