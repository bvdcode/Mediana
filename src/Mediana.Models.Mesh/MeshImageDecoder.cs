// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Memory;
using Mediana.Core.Metadata;
using Mediana.Core.Video;
using Mediana.Drawing;

namespace Mediana.Models.Mesh
{
    /// <summary>Recognizes STL/OBJ geometry and renders an owned preview. Register ThreeMfPageDecoder with the OPC decoder for 3MF.</summary>
    public class MeshImageDecoder(MeshRenderOptions? renderOptions = null, StlReaderOptions? stlOptions = null, ObjReaderOptions? objOptions = null) : IImageDecoder, IImageInfoReader
    {
        public string ImplementationId => "mesh.managed.image";
        public int ProbeLength => MeshRecognition.ProbeBytes;
        public ImageDecoderFeatures Features => ImageDecoderFeatures.SingleImage;

        public bool CanDecode(ReadOnlySpan<byte> prefix, long? encodedLength = null) => MeshRecognition.Recognize(prefix, encodedLength) is MeshFormat.Stl or MeshFormat.Obj;

        /// <summary>Reports the recognized model's configured preview canvas without rasterizing geometry.</summary>
        public ValueTask<ImageInfo> ReadInfoAsync(ReadOnlyMemory<byte> encoded, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MeshRenderOptions options = renderOptions ?? new();
            options.Validate();
            MeshFormat format = MeshRecognition.Recognize(encoded.Span[..Math.Min(encoded.Length, ProbeLength)], encoded.Length);
            string id = format switch
            {
                MeshFormat.Stl => "stl",
                MeshFormat.Obj => "obj",
                MeshFormat.Unknown or MeshFormat.ThreeMf => throw new NotSupportedException("STL or OBJ geometry is required."),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            return ValueTask.FromResult(new ImageInfo(id, options.Width, options.Height, ImageDimensionUnit.Pixels, isPreviewCanvas: true));
        }

        public async ValueTask<VideoFrame> DecodeAsync(ReadOnlyMemory<byte> encoded, ImageDecodeRequest request,
            MediaBufferAllocator allocator, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allocator);
            request.Validate(Features);
            MeshFormat format = MeshRecognition.Recognize(encoded.Span[..Math.Min(encoded.Length, ProbeLength)], encoded.Length);
            TriangleMesh mesh;
            switch (format)
            {
                case MeshFormat.Stl:
                    mesh = StlReader.Read(encoded, stlOptions, cancellationToken);
                    break;
                case MeshFormat.Obj:
                    mesh = await ObjReader.ReadAsync(encoded, objOptions, cancellationToken).ConfigureAwait(false);
                    break;
                case MeshFormat.ThreeMf:
                case MeshFormat.Unknown:
                    throw new NotSupportedException("The mesh image decoder requires STL or OBJ geometry.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(format));
            }
            using PixelBuffer pixels = MeshRenderer.Render(mesh, renderOptions, cancellationToken, allocator);
            return pixels.PublishVideoFrame(cancellationToken);
        }
    }
}
