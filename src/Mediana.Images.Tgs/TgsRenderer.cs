// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System;
using System.Linq;
using System.Threading;
using Mediana.Core.Memory;

namespace Mediana.Images.Tgs
{
    /// <summary>Renders one selected TGS/Lottie frame on the managed vector canvas.</summary>
    public static partial class TgsRenderer
    {
        /// <summary>Renders an absolute frame number in [InPoint, OutPoint). Fractional frames are supported.</summary>
        public static PixelBuffer RenderFrame(TgsDocument document, double frame, DrawingRenderOptions? options = null,
            CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
            => RenderFrameDetailed(document, frame, options, cancellationToken, allocator).Image;

        /// <summary>Renders one frame with timing and detected feature diagnostics. The allocator counts final output storage.</summary>
        public static TgsFrame RenderFrameDetailed(TgsDocument document, double frame, DrawingRenderOptions? options = null,
            CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new DrawingRenderOptions();
            cancellationToken.ThrowIfCancellationRequested();
            if (!double.IsFinite(frame) || frame < document.InPoint || frame >= document.OutPoint)
            {
                throw new ArgumentOutOfRangeException(nameof(frame));
            }

            if (!options.AllowPartialRendering && document.UnsupportedFeatures.Count > 0)
            {
                throw new NotSupportedException("Unsupported TGS/Lottie features: " + string.Join("; ", document.UnsupportedFeatures));
            }

            TgsSceneBuilder builder = new(document, cancellationToken);
            SceneNode scene = builder.Build(frame, options.AllowPartialRendering);
            return new TgsFrame
            {
                SourceFrame = frame,
                Timestamp = TimeSpan.FromSeconds((frame - document.InPoint) / document.FrameRate),
                Duration = TimeSpan.FromSeconds(Math.Min(1, document.OutPoint - frame) / document.FrameRate),
                Warnings = Array.AsReadOnly(document.UnsupportedFeatures.Concat(builder.Warnings).Distinct().Order().ToArray()),
                Image = SceneRasterizer.Render(scene, document.Width, document.Height, options,
                    (width, height) => Viewport(document, width, height), cancellationToken, allocator)
            };
        }

        private static System.Numerics.Matrix3x2 Viewport(TgsDocument document, int width, int height)
        {
            float scale = Math.Min(width / (float)document.Width, height / (float)document.Height);
            return System.Numerics.Matrix3x2.CreateScale(scale) * System.Numerics.Matrix3x2.CreateTranslation((width - (document.Width * scale)) / 2, (height - (document.Height * scale)) / 2);
        }

        /// <summary>Renders the frame at an elapsed time from the composition's in-point.</summary>
        public static PixelBuffer RenderAtTime(TgsDocument document, double seconds, DrawingRenderOptions? options = null,
            CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
        {
            ArgumentNullException.ThrowIfNull(document);
            return RenderFrame(document, document.InPoint + (seconds * document.FrameRate), options, cancellationToken, allocator);
        }
    }
}
