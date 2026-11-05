// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Documents.Word
{
    /// <summary>Managed Word pagination and direct page rasterization, without intermediate document formats.</summary>
    public static class WordRenderer
    {
        /// <summary>Resolves fonts and paginates once. The returned layout supports concurrent independent rendering.</summary>
        public static WordLayout Layout(WordDocument document, WordLayoutOptions? options = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(document);
            options ??= new();
            if (options.MaximumPages <= 0 || options.MaximumGlyphs <= 0 || options.MaximumPathCommands <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<string> warnings = new(document.Warnings, StringComparer.Ordinal);
            WordFonts fonts = new(options, warnings, cancellationToken);
            WordTextLayouter text = new(fonts, cancellationToken);
            WordFlowBuilder flow = new(text, cancellationToken);
            WordPaginator paginator = new(options.MaximumPages, cancellationToken);
            IReadOnlyList<WordPaginatedPage> body = paginator.Paginate(document.Sections, flow);
            Dictionary<(IReadOnlyList<WordBlock> Blocks, double Width), (IReadOnlyList<WordPaint> Paint, double Height)> marginCache = [];
            List<WordLayoutPage> pages = [];
            for (int index = 0; index < body.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WordPaginatedPage page = body[index];
                WordSection section = page.Section;
                WordPageGeometry geometry = section.Geometry;
                double bodyWidth = geometry.Width - geometry.Left - geometry.Right;
                List<WordPaint> commands = new(page.Paint);
                if (!page.Blank) { AddMargin(section.Header, false); AddMargin(section.Footer, true); }
                pages.Add(new(section.PageSize, commands.AsReadOnly()));

                void AddMargin(WordMarginContent margin, bool footer)
                {
                    IReadOnlyList<WordBlock> blocks;
                    if (page.FirstInSection && section.DistinctFirstPage) { blocks = margin.First; }
                    else if (index % 2 == 1 && section.DistinctEvenPages) { blocks = margin.Even; }
                    else { blocks = margin.Default; }
                    if (blocks.Count == 0) { return; }
                    if (!marginCache.TryGetValue((blocks, bodyWidth), out (IReadOnlyList<WordPaint> Paint, double Height) placed))
                    {
                        placed = WordFlowBuilder.Stack(flow.Build(blocks, bodyWidth));
                        marginCache.Add((blocks, bodyWidth), placed);
                    }
                    double y = footer ? geometry.Height - geometry.Footer - placed.Height : geometry.Header;
                    commands.Add(new WordGroupPaint(placed.Paint, geometry.Left, y));
                }
            }
            return new(pages.AsReadOnly(), Array.AsReadOnly(warnings.ToArray()));
        }

        /// <summary>Renders a one-based page from a reusable layout. The caller owns and disposes the returned pixels.
        /// Output, supersampling surfaces and decoded pictures share the supplied allocation budget.</summary>
        public static async ValueTask<PixelBuffer> RenderAsync(WordLayout layout, int pageNumber, WordRenderOptions? options = null,
            CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null)
        {
            ArgumentNullException.ThrowIfNull(layout);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(pageNumber, layout.PageCount);
            options ??= new();
            WordRenderPlan plan = WordRenderPlan.Create(layout.GetPageSize(pageNumber), options);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (string warning in layout.Warnings) { options.WarningHandler?.Invoke(warning); }
            cancellationToken.ThrowIfCancellationRequested();
            allocator ??= new(long.MaxValue);
            PixelBuffer output = new(plan.Width, plan.Height, allocator);
            bool completed = false;
            try
            {
                int samples = options.Supersampling;
                using PixelBuffer? supersampled = samples == 1 ? null : new(plan.Width * samples, plan.Height * samples, allocator);
                PixelBuffer surface = supersampled ?? output;
                surface.Clear(options.Background);
                RasterCanvas canvas = new(surface);
                using WordPageRasterizer rasterizer = new(canvas, allocator, options, cancellationToken);
                await rasterizer.DrawAsync(layout.Pages[pageNumber - 1].Paint, Matrix3x2.CreateScale((float)(plan.Scale * samples)));
                cancellationToken.ThrowIfCancellationRequested();
                supersampled?.DownsampleTo(output, samples, cancellationToken);
                completed = true;
                return output;
            }
            finally
            {
                if (!completed) { output.Dispose(); }
            }
        }

        /// <summary>Paginates and renders one page. Reuse <see cref="Layout"/> when rendering multiple pages.</summary>
        public static ValueTask<PixelBuffer> RenderAsync(WordDocument document, int pageNumber, WordRenderOptions? options = null,
            WordLayoutOptions? layoutOptions = null, CancellationToken cancellationToken = default, MediaBufferAllocator? allocator = null) =>
            RenderAsync(Layout(document, layoutOptions, cancellationToken), pageNumber, options, cancellationToken, allocator);
    }
}
