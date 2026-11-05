// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Video;
using System;
using System.Collections.Generic;

namespace Mediana.Images.Svg
{
    /// <summary>An interpreted SVG scene independent of its input stream. Safe for concurrent rendering.</summary>
    public class SvgDocument : IDisposable
    {
        private readonly SceneNode _root;
        private VideoFrame[]? _images;
        internal SceneNode Root { get { ObjectDisposedException.ThrowIf(_images is null, this); return _root; } }
        internal string AspectRatio { get; }
        internal (float X, float Y, float Width, float Height) ViewBox { get; }
        /// <summary>Gets the intrinsic viewport width in CSS pixels (96 DPI).</summary>
        public float Width { get; }
        /// <summary>Gets the intrinsic viewport height in CSS pixels (96 DPI).</summary>
        public float Height { get; }
        /// <summary>Gets explicitly detected features that this renderer cannot paint.</summary>
        public IReadOnlyList<string> UnsupportedFeatures { get; }

        internal SvgDocument(SceneNode root, float width, float height, (float, float, float, float) box, string aspect, string[] warnings, VideoFrame[] images)
        {
            _root = root;
            _images = images;
            Width = width;
            Height = height;
            ViewBox = box;
            AspectRatio = aspect;
            UnsupportedFeatures = System.Array.AsReadOnly(warnings);
        }

        /// <summary>Releases retained raster images. Complete all concurrent renders before disposal.</summary>
        public void Dispose()
        {
            VideoFrame[]? images = Interlocked.Exchange(ref _images, null);
            if (images is not null) { foreach (VideoFrame image in images) { image.Dispose(); } }
        }
    }
}
