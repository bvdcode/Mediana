// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Numerics;
namespace Mediana.Documents.Pdf
{
    /// <summary>Owns PDF parsing state. Detached pages and their image pixels survive disposal.</summary>
    public class PdfDocument : IDisposable
    {
        private PdfStorage? _storage;
        private readonly object _sync = new();
        private readonly List<PdfPageSource> _pages = [];
        public int PageCount => _pages.Count;
        internal PdfDocument(PdfStorage storage, CancellationToken token)
        {
            _storage = storage;
            PdfValue catalog = storage.Resolve(storage.Trailer.Get("Root"), token);
            if (catalog.Get("Type").Name() != "Catalog") { throw new InvalidDataException("Missing PDF catalog."); }
            if (catalog.Get("OCProperties").Kind != PdfValueKind.Null) { throw new NotSupportedException("PDF optional-content configurations are not supported."); }
            Walk(catalog.Get("Pages"), PdfValue.Null, PdfValue.Null, PdfValue.Null, 0, new HashSet<PdfValue>(), 0, storage, token);
        }
        private void Walk(PdfValue node, PdfValue resources, PdfValue media, PdfValue crop, int rotation, HashSet<PdfValue> visited, int depth, PdfStorage storage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            PdfValue value = storage.Resolve(node, token);
            if (depth >= storage.Budget.Options.MaximumDepth || !visited.Add(value)) { throw new InvalidDataException("Cyclic or excessive PDF page tree."); }
            PdfValue Read(string key) => storage.Resolve(value.Get(key), token);
            if (value.Get("Resources").Kind != PdfValueKind.Null) { resources = Read("Resources"); }
            if (value.Get("MediaBox").Kind != PdfValueKind.Null) { media = Read("MediaBox"); }
            if (value.Get("CropBox").Kind != PdfValueKind.Null) { crop = Read("CropBox"); }
            if (value.Get("Rotate").Kind != PdfValueKind.Null) { rotation = Read("Rotate").Integer(); }
            string type = Read("Type").Name();
            if (type == "Pages")
            {
                PdfValue children = Read("Kids");
                if (children.Kind != PdfValueKind.Array) { throw new InvalidDataException("Missing PDF page children."); }
                foreach (PdfValue child in children.Items) { Walk(child, resources, media, crop, rotation, visited, depth + 1, storage, token); }
            }
            else if (type == "Page")
            {
                if (_pages.Count >= storage.Budget.Options.MaximumPages) { throw new InvalidDataException("PDF page count exceeds its limit."); }
                double[] box = Box(media), visible = crop.Kind == PdfValueKind.Null ? box : Box(crop);
                double left = Math.Max(box[0], visible[0]), bottom = Math.Max(box[1], visible[1]), right = Math.Min(box[2], visible[2]), top = Math.Min(box[3], visible[3]);
                double unit = value.Get("UserUnit").Kind == PdfValueKind.Null ? 1 : Read("UserUnit").Numeric();
                if (!(right > left && top > bottom) || unit <= 0 || unit > 75000 || rotation % 90 != 0) { throw new InvalidDataException("Invalid PDF page geometry."); }
                rotation = ((rotation % 360) + 360) % 360;
                double w = right - left, h = top - bottom;
                Matrix3x2 rotate = rotation switch
                {
                    0 => Matrix3x2.Identity,
                    90 => new(0, -1, 1, 0, 0, (float)w),
                    180 => new(-1, 0, 0, -1, (float)w, (float)h),
                    270 => new(0, 1, -1, 0, (float)h, 0),
                    _ => throw new InvalidDataException("Unsupported page rotation.")
                };
                Matrix3x2 transform = Matrix3x2.CreateTranslation((float)-left, (float)-bottom) * rotate * Matrix3x2.CreateScale((float)unit);
                double width = (rotation is 90 or 270 ? h : w) * unit, height = (rotation is 90 or 270 ? w : h) * unit;
                if (!double.IsFinite(width + height) || width > float.MaxValue || height > float.MaxValue) { throw new InvalidDataException("PDF page is too large."); }
                _pages.Add(new(value, resources, new(_pages.Count + 1, width, height, rotation), transform));
            }
            else { throw new InvalidDataException("Unexpected PDF page tree node."); }
        }
        internal static double[] Box(PdfValue value)
        {
            if (value.Kind != PdfValueKind.Array || value.Items.Length != 4) { throw new InvalidDataException("Invalid PDF rectangle."); }
            double[] box = value.Items.Select(x => x.Numeric()).ToArray();
            return [Math.Min(box[0], box[2]), Math.Min(box[1], box[3]), Math.Max(box[0], box[2]), Math.Max(box[1], box[3])];
        }
        public PdfPageInfo GetPageInfo(int pageNumber)
        {
            lock (_sync) { RequireOpen(); return Page(pageNumber).Info; }
        }
        public PdfPage GetPage(int pageNumber, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                PdfStorage storage = RequireOpen();
                cancellationToken.ThrowIfCancellationRequested();
                return new PdfInterpreter(storage, cancellationToken).Interpret(Page(pageNumber));
            }
        }
        private PdfPageSource Page(int number)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(number, 1); ArgumentOutOfRangeException.ThrowIfGreaterThan(number, PageCount);
            return _pages[number - 1];
        }
        private PdfStorage RequireOpen() => _storage ?? throw new ObjectDisposedException(nameof(PdfDocument));
        public void Dispose() { lock (_sync) { _storage = null; _pages.Clear(); } }
    }
}
