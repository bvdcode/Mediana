// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Metadata;
using Mediana.IO;
using System.Xml;
using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    public static partial class SvgReader
    {
        /// <summary>Reads the root viewport without interpreting geometry or loading external resources.
        /// Dimensions follow the same intrinsic viewport rules as rendering.</summary>
        public static ImageInfo ReadInfo(ReadOnlyMemory<byte> xml, SvgReaderOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new SvgReaderOptions();
            Validate(options);
            cancellationToken.ThrowIfCancellationRequested();
            if (xml.Length > options.MaximumInputBytes)
            {
                throw new InvalidDataException("SVG exceeds MaximumInputBytes.");
            }
            using ReadOnlyMemoryStream source = new(xml);
            using XmlReader reader = XmlReader.Create(source, Settings(options));
            if (reader.MoveToContent() != XmlNodeType.Element)
            {
                throw new InvalidDataException("Expected an SVG root element.");
            }
            XElement root = new(XName.Get(reader.LocalName, reader.NamespaceURI));
            while (reader.MoveToNextAttribute())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NamespaceURI.Length == 0 && reader.LocalName is "width" or "height" or "viewBox")
                {
                    root.Add(new XAttribute(reader.LocalName, reader.Value));
                }
            }
            SvgViewport viewport = SvgViewport.Read(root);
            return new ImageInfo("svg", viewport.Width, viewport.Height, ImageDimensionUnit.Pixels);
        }
    }
}
