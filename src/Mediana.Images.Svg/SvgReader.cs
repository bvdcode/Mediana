// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.IO;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Mediana.Images.Svg
{
    /// <summary>Reads SVG without executing scripts or loading external resources.</summary>
    public static partial class SvgReader
    {
        /// <summary>Parses SVG XML and interprets vector geometry.</summary>
        public static SvgDocument Read(string xml, SvgReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(xml);
            options ??= new SvgReaderOptions();
            Validate(options);
            cancellationToken.ThrowIfCancellationRequested();
            if (Encoding.UTF8.GetByteCount(xml) > options.MaximumInputBytes)
            {
                throw new InvalidDataException("SVG exceeds MaximumInputBytes.");
            }

            using StringReader? source = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(source, Settings(options));
            XElement root = XElement.Load(reader);
            return new SvgSceneBuilder(root, options, cancellationToken).Build();
        }

        /// <summary>Parses borrowed XML bytes using their BOM and encoding declaration. The returned scene owns its data.</summary>
        public static SvgDocument Read(ReadOnlyMemory<byte> xml, SvgReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new SvgReaderOptions();
            return new SvgSceneBuilder(ReadRoot(xml, options, cancellationToken), options, cancellationToken).Build();
        }

        /// <summary>Parses borrowed XML and asynchronously decodes referenced image resources. The returned scene owns its data.</summary>
        public static ValueTask<SvgDocument> ReadAsync(ReadOnlyMemory<byte> xml, SvgReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            options ??= new SvgReaderOptions();
            return new SvgSceneBuilder(ReadRoot(xml, options, cancellationToken), options, cancellationToken).BuildAsync();
        }

        private static XElement ReadRoot(ReadOnlyMemory<byte> xml, SvgReaderOptions options, CancellationToken token)
        {
            Validate(options);
            token.ThrowIfCancellationRequested();
            if (xml.Length > options.MaximumInputBytes) { throw new InvalidDataException("SVG exceeds MaximumInputBytes."); }
            using ReadOnlyMemoryStream source = new(xml);
            using XmlReader reader = XmlReader.Create(source, Settings(options));
            return XElement.Load(reader);
        }

        /// <summary>Reads bounded SVG input and leaves the source stream open.</summary>
        public static async Task<SvgDocument> ReadAsync(Stream source, SvgReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(source);
            options ??= new SvgReaderOptions();
            Validate(options);
            byte[] data = await BoundedStreamReader.ReadAsync(source, options.MaximumInputBytes, cancellationToken).ConfigureAwait(false);
            return await ReadAsync(data, options, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Reads a local SVG file.</summary>
        public static async Task<SvgDocument> ReadFileAsync(string path, SvgReaderOptions? options = null, CancellationToken cancellationToken = default)
        {
            await using FileStream? source = File.OpenRead(path);
            return await ReadAsync(source, options, cancellationToken).ConfigureAwait(false);
        }

        private static XmlReaderSettings Settings(SvgReaderOptions options) => new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersInDocument = options.MaximumInputBytes
        };

        private static void Validate(SvgReaderOptions o)
        {
            if (o.MaximumInputBytes <= 0 || o.MaximumElements <= 0 || o.MaximumPathCommands <= 0 || o.MaximumDepth < 1 || o.MaximumDepth > 64 || o.MaximumImagePixels <= 0 || o.MaximumImageBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(o));
            }
        }
    }
}
