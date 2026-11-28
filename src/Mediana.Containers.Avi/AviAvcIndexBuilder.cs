// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core;
using Mediana.Core.Memory;
using Mediana.Formats;
using Mediana.Formats.Video;
using Mediana.IO;

namespace Mediana.Containers.Avi
{
    internal class AviAvcIndexBuilder(IRandomAccessByteSource source, MediaBufferAllocator allocator, AviTrack track)
    {
        private readonly PriorityQueue<long, int> pending = new();
        private int? previousOrder;
        private long presentationIndex;

        public async ValueTask BuildAsync(CancellationToken token)
        {
            AviPacketIndex packets = track.Packets;
            if (packets.Count == 0) { return; }
            AviAvcParameterSets parameters = new(track.Stream.Configuration);
            using AviAvcHeaderProbe probe = new(source, allocator);
            H264PictureOrderState order = new();
            H264SequenceParameterSet? limitedSequence = null;
            int reorderFrames = 0;

            for (long index = 0; index < packets.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                AviPacketEntry packet = packets[index];
                parameters.BeginPacket();
                H264SliceHeader picture = await probe.ReadAsync(packet, parameters, index == 0, token).ConfigureAwait(false);
                if (picture.Layout.IsField)
                {
                    throw new NotSupportedException("AVI AVC timing requires one frame picture per chunk.");
                }
                if (index == 0)
                {
                    EncodedStreamFormat format = parameters.LengthSize == 0 ? StreamFormats.AvcAnnexBAccessUnit : StreamFormats.Avc;
                    track.SetRepresentation(format, parameters.SaveOpening());
                }

                bool reset = H264FieldPairing.ResetsReferences(picture);
                if (picture.IsIdr || reset)
                {
                    if (picture.IsIdr && picture.NoOutputOfPriorPictures && pending.Count != 0)
                    {
                        throw new NotSupportedException("The AVI AVC stream suppresses pictures from its preceding sequence.");
                    }
                    while (pending.Count > 0) { PublishNext(packets); }
                    previousOrder = null;
                }
                order = order.Advance(picture);
                if (limitedSequence != picture.Sequence)
                {
                    limitedSequence = picture.Sequence;
                    reorderFrames = H264PictureBufferLimits.Read(limitedSequence).ReorderFrames;
                }
                packets.Replace(index, packet with { KeyFrame = picture.IsIdr && parameters.CanRestart(picture) });
                pending.Enqueue(index, order.Current.Value);
                while (pending.Count > reorderFrames) { PublishNext(packets); }
            }
            while (pending.Count > 0) { PublishNext(packets); }
        }

        private void PublishNext(AviPacketIndex packets)
        {
            if (!pending.TryDequeue(out long index, out int order))
            {
                throw new InvalidOperationException("An AVI AVC presentation slot has no pending picture.");
            }
            if (previousOrder is int previous && order <= previous)
            {
                throw new InvalidDataException("The AVI AVC picture order disagrees with the declared reorder depth.");
            }
            previousOrder = order;
            AviPacketEntry slot = packets[presentationIndex++];
            packets.Replace(index, packets[index] with { PresentationTime = slot.Time, PresentationDuration = slot.Duration });
        }
    }
}
