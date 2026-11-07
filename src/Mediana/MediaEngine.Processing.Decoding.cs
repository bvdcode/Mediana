// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core;
using Mediana.Execution;

namespace Mediana
{
    public partial class MediaEngine
    {
        private static MediaDecodePipeline<TFrame> CreateProcessingDecoder<TFrame>(EncodedStreamInfo stream,
            IReadOnlyList<IDecoderFactory<TFrame>> factories, MediaTrackInput input) where TFrame : class, IDisposable
        {
            IDecoderFactory<TFrame> factory = SelectDecoder(stream, factories);
            IMediaCodec<MediaPacket, TFrame> decoder = factory.Create(stream, input.InputAllocator);
            bool owned = false;
            try
            {
                IRandomAccessInspector? inspector = (factory as IRandomAccessDecoderFactory)?.CreateRandomAccessInspector(stream);
                MediaDecodePipeline<TFrame> pipeline = new(input.Reader, decoder, stream.StreamIndex,
                    randomAccessInspector: inspector, sharedReadBudget: input.Budget);
                input.Resources.Own(pipeline);
                owned = true;
                return pipeline;
            }
            finally { if (!owned) { decoder.Dispose(); } }
        }
    }
}
