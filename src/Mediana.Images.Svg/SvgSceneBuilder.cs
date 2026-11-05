// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using Mediana.Core.Memory;
using Mediana.Core.Video;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    internal partial class SvgSceneBuilder
    {
        private readonly XElement _root;
        private readonly TextRenderer _text;
        private readonly MediaBufferAllocator _imageAllocator;
        private readonly SvgReaderOptions _limits;
        private readonly CancellationToken _token;
        private readonly Dictionary<string, XElement> _ids = new(StringComparer.Ordinal);
        private readonly HashSet<XElement> _active = new();
        private readonly Dictionary<XElement, SceneGradient?> _gradients = new();
        private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
        private readonly List<(string Selector, int Specificity, Dictionary<string, string> Values)> _rules = new();
        private float _width;
        private float _height;
        private int _elements;
        private int _commands;
        private static readonly HashSet<string> Definitions = new("defs symbol clipPath mask filter pattern marker linearGradient radialGradient style title desc metadata".Split(' '));
        private static readonly HashSet<string> Containers = new("g svg a symbol clipPath mask".Split(' '));
        private static readonly HashSet<string> Metadata = new("title desc metadata namedview".Split(' '));
        private static readonly HashSet<string> Inherited = new("fill stroke color fill-opacity stroke-opacity fill-rule clip-rule stroke-width stroke-linecap stroke-linejoin stroke-miterlimit stroke-dasharray stroke-dashoffset visibility font-family font-size font-weight font-style font-variant font-kerning font-feature-settings text-anchor letter-spacing word-spacing dominant-baseline alignment-baseline baseline-shift text-decoration direction unicode-bidi writing-mode white-space color-interpolation color-interpolation-filters".Split(' '));
        private static readonly HashSet<string> Supported = new(Inherited.Concat("opacity display clip-path stop-color stop-opacity mask mask-type filter color-interpolation color-interpolation-filters mix-blend-mode isolation flood-color flood-opacity lighting-color".Split(' ')));
        internal SvgSceneBuilder(XElement root, SvgReaderOptions limits, CancellationToken token)
        {
            _root = root;
            _text = new(limits.Text);
            _imageAllocator = limits.ImageAllocator ?? new(limits.MaximumImageBytes);
            _limits = limits;
            _token = token;
        }

        private bool _synchronous;

        internal SvgDocument Build()
        {
            ValueTask<SvgDocument> operation = BuildAsync(true);
            if (!operation.IsCompleted) { throw new InvalidOperationException("Synchronous SVG construction must complete inline."); }
            return operation.GetAwaiter().GetResult();
        }

        internal async ValueTask<SvgDocument> BuildAsync(bool synchronous = false)
        {
            _synchronous = synchronous;
            if (_root.Name.LocalName != "svg" || !IsSvgElement(_root))
            {
                throw new InvalidDataException("Expected an SVG root element.");
            }

            bool completed = false;
            try
            {
                Index(_root, 0);
                SvgDocument document = await BuildViewportAsync().ConfigureAwait(false);
                completed = true;
                return document;
            }
            finally
            {
                if (!completed) { foreach (VideoFrame? image in _images.Values) { image?.Dispose(); } }
            }
        }

        private async ValueTask<SvgDocument> BuildViewportAsync()
        {
            SvgViewport viewport = SvgViewport.Read(_root);
            _width = viewport.ViewBox.Width;
            _height = viewport.ViewBox.Height;
            SceneNode node = await NodeAsync(_root, new Dictionary<string, string>(), 0, true);
            return new SvgDocument(node, viewport.Width, viewport.Height, viewport.ViewBox, (string?)_root.Attribute("preserveAspectRatio") ?? "xMidYMid meet", _warnings.Order().ToArray(), _images.Values.OfType<VideoFrame>().ToArray());
        }

        private static bool IsSvgElement(XElement element) => element.Name.NamespaceName.Length == 0 || element.Name.NamespaceName == "http://www.w3.org/2000/svg";

        private void Index(XElement element, int depth, bool svgContent = true)
        {
            Count(depth);
            svgContent = svgContent && IsSvgElement(element) && !Metadata.Contains(element.Name.LocalName);
            if (svgContent)
            {
                if (element.Attribute("id") is { } id && !_ids.TryAdd(id.Value, element))
                {
                    throw new InvalidDataException("Duplicate SVG id: " + id.Value);
                }

                if (element.Name.LocalName == "style")
                {
                    StyleSheet(element.Value);
                }
            }

            foreach (XElement child in element.Elements().ToArray())
            {
                Index(child, depth + 1, svgContent);
            }

            if (!svgContent)
            {
                element.Remove();
            }
        }

        private void Count(int depth)
        {
            _token.ThrowIfCancellationRequested();
            if (++_elements > _limits.MaximumElements || depth >= _limits.MaximumDepth)
            {
                throw new InvalidDataException("SVG exceeds element/reference nesting limits.");
            }
        }

        private async ValueTask<SceneNode> NodeAsync(XElement e, Dictionary<string, string> inherited, int depth, bool definition = false)
        {
            Count(depth);
            if (!_active.Add(e))
            {
                throw new InvalidDataException("Cyclic SVG reference.");
            }

            try
            {
                return await BuildNodeAsync(e, inherited, depth, definition).ConfigureAwait(false);
            }
            finally
            {
                _active.Remove(e);
            }
        }

        private async ValueTask<SceneNode> BuildNodeAsync(XElement e, Dictionary<string, string> inherited, int depth, bool definition)
        {
            if (!IsSvgElement(e))
            {
                return Empty;
            }

            Dictionary<string, string> style = Style(e, inherited);
            if (Value(style, "display", "inline") == "none")
            {
                return Empty;
            }

            string name = e.Name.LocalName;
            if (name == "svg" && e != _root)
            {
                Warn("Nested SVG viewport");
            }

            if (!definition && Definitions.Contains(name))
            {
                return Empty;
            }

            Matrix3x2 transform = SvgNumbers.Transform((string?)e.Attribute("transform"));
            var children = new List<SceneNode>();
            VectorPath? path = Shape(e);
            VideoFrame? image = name == "image" ? await ImageAsync(Href(e)).ConfigureAwait(false) : null;
            await PopulateAsync(e, name, style, path, children, depth).ConfigureAwait(false);
            if (path is not null)
            {
                path = ValidatePath(path, style);
            }

            SceneClip? clip = await ClipAsync(Value(style, "clip-path", "none"), depth);
            SceneNode node = new SceneNode(path, Paint(Value(style, "fill", "black"), style), Paint(Value(style, "stroke", "none"), style), Pen(style), Rule(Value(style, "fill-rule", "nonzero")), Rule(Value(style, "clip-rule", "nonzero")), transform, Opacity(style, "opacity"), Opacity(style, "fill-opacity"), Opacity(style, "stroke-opacity"), clip, children.ToArray())
            {
                Mask = await MaskAsync(Value(style, "mask", "none"), depth),
                Filter = await FilterAsync(Value(style, "filter", "none"), style, depth),
                BlendMode = Blend(Value(style, "mix-blend-mode", "normal"))
            };
            return PlaceImage(node, e, image);
        }

        private SceneNode PlaceImage(SceneNode node, XElement element, VideoFrame? image)
        {
            if (image is null)
            {
                return node;
            }

            return node with
            {
                Image = image,
                ImageTransform = ImagePlacement(element, image),
                ImageViewport = new VectorPath().Rectangle(Length(element, "x", 0, _width), Length(element, "y", 0, _height), Length(element, "width", image.Format.Width, _width), Length(element, "height", image.Format.Height, _height))
            };
        }

        private async ValueTask PopulateAsync(XElement e, string name, Dictionary<string, string> style, VectorPath? path, List<SceneNode> children, int depth)
        {
            if (name == "use")
            {
                await UseAsync(e, style, children, depth).ConfigureAwait(false);
            }
            else if (name == "text")
            {
                children.AddRange(await TextNodesAsync(e, style, depth).ConfigureAwait(false));
            }
            else if (Containers.Contains(name))
            {
                foreach (XElement child in e.Elements())
                {
                    children.Add(await NodeAsync(child, style, depth + 1).ConfigureAwait(false));
                }
            }
            else if (path is null && name != "image" && !Metadata.Contains(name))
            {
                Warn("SVG element: " + name);
            }
        }

        private VectorPath? ValidatePath(VectorPath path, Dictionary<string, string> style)
        {
            _commands = checked(_commands + path.CommandCount);
            if (_commands > _limits.MaximumPathCommands)
            {
                throw new InvalidDataException("SVG exceeds MaximumPathCommands.");
            }

            return Value(style, "visibility", "visible") is "hidden" or "collapse" ? null : path;
        }

        private static SceneNode Empty => new(null, null, null, new StrokeStyle(), FillRule.NonZero, FillRule.NonZero, Matrix3x2.Identity, 1, 1, 1, null, Array.Empty<SceneNode>());

        private async ValueTask UseAsync(XElement e, Dictionary<string, string> style, List<SceneNode> children, int depth)
        {
            XElement? target = Reference(Href(e));
            if (target is null)
            {
                return;
            }

            SceneNode node = await NodeAsync(target, style, depth + 1, true);
            Matrix3x2 placement = Matrix3x2.CreateTranslation(Length(e, "x", 0, _width), Length(e, "y", 0, _height));
            if (target.Name.LocalName == "symbol" && target.Attribute("viewBox") is { } box)
            {
                float[] v = SvgNumbers.List(box.Value);
                if (v.Length != 4 || v[2] <= 0 || v[3] <= 0)
                {
                    throw new InvalidDataException("Invalid symbol viewBox.");
                }

                placement = SvgRenderer.Viewport((v[0], v[1], v[2], v[3]), Length(e, "width", _width, _width), Length(e, "height", _height, _height), (string?)target.Attribute("preserveAspectRatio") ?? "xMidYMid meet") * placement;
            }

            children.Add(node with { Transform = node.Transform * placement });
        }

        private async ValueTask<SceneClip?> ClipAsync(string value, int depth)
        {
            if (value == "none")
            {
                return null;
            }

            XElement? target = Reference(Url(value));
            if (target is null)
            {
                return null;
            }

            if (target.Name.LocalName != "clipPath")
            {
                Warn("Non-clipPath clipping reference");
                return null;
            }

            SceneNode node = await NodeAsync(target, new Dictionary<string, string>(), depth + 1, true);
            var shapes = new List<SceneClipShape>();
            try
            {
                node.ClipShapes(node.Transform, shapes);
            }
            catch (NotSupportedException ex)
            {
                Warn(ex.Message);
            }

            return new SceneClip(shapes.ToArray(), (string?)target.Attribute("clipPathUnits") == "objectBoundingBox");
        }

        private XElement? Reference(string? href)
        {
            if (href is null)
            {
                Warn("Missing SVG reference");
                return null;
            }

            if (!href.StartsWith('#'))
            {
                Warn("External SVG resource");
                return null;
            }

            if (_ids.TryGetValue(href[1..], out XElement? e))
            {
                return e;
            }

            Warn("Missing SVG reference " + href);
            return null;
        }

        private static string? Href(XElement e) => (string?)e.Attribute("href") ?? (string?)e.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink"));
        private static string? Url(string value) => value.StartsWith("url(", StringComparison.Ordinal) && value.EndsWith(')') ? value[4..^1].Trim().Trim('\'', '"') : null;
        private void Warn(string text) => _warnings.Add(text);
        private static FillRule Rule(string value) => value == "evenodd" ? FillRule.EvenOdd : FillRule.NonZero;
        private static string Value(Dictionary<string, string> values, string name, string fallback) => values.GetValueOrDefault(name, fallback);
        private static float Opacity(Dictionary<string, string> values, string name) => Math.Clamp(SvgLength.Parse(Value(values, name, "1")), 0, 1);
        private static float Length(XElement e, string name, float fallback, float percent) => e.Attribute(name) is { } a ? SvgLength.Parse(a.Value, percent) : fallback;
    }
}
