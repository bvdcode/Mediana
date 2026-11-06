// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Core.Memory;
using Mediana.Core.Video;
using Mediana.Drawing;
using System.Numerics;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal partial class PresentationRasterizer(RasterCanvas canvas, PresentationDocument document, PresentationScene scene,
        PresentationRenderOptions options, MediaBufferAllocator allocator, CancellationToken token) : IDisposable
    {
        private readonly Dictionary<string, VideoFrame> _images = new(StringComparer.Ordinal);
        private readonly TextRenderer _text = new(options.Text);
        private int _shapes, _glyphs;
        internal async ValueTask DrawAsync(Matrix3x2 transform)
        {
            XElement? background = Background(scene.Slide) ?? Background(scene.Layout) ?? Background(scene.Master);
            if (background is not null) { Paint(new VectorPath().Rectangle(0,0,(float)document.Width,(float)document.Height), background, transform); }
            bool masterShapes = PresentationXml.Boolean(scene.Slide.Root,"showMasterSp",true) && PresentationXml.Boolean(scene.Layout?.Root,"showMasterSp",true);
            if (masterShapes && scene.Master?.Tree is XElement master) { await TreeAsync(scene.Master, master, transform, true); }
            if (scene.Layout?.Tree is XElement layout) { await TreeAsync(scene.Layout, layout, transform, true); }
            if (scene.Slide.Tree is not XElement tree) { throw new InvalidDataException("Presentation slide has no shape tree."); }
            await TreeAsync(scene.Slide, tree, transform, false);
        }
        private static XElement? Background(PresentationPart? part) => part?.Root.Element(part.Root.Name.Namespace + "cSld")?.Element(part.Root.Name.Namespace + "bg")?.Elements().FirstOrDefault();
        private async ValueTask TreeAsync(PresentationPart part, XElement tree, Matrix3x2 parent, bool skipPlaceholders)
        {
            XNamespace p = tree.Name.Namespace;
            foreach (XElement element in tree.Elements())
            {
                token.ThrowIfCancellationRequested();
                if (++_shapes > document.Options.MaximumShapes) { throw new InvalidDataException("Presentation exceeds MaximumShapes."); }
                if (skipPlaceholders && PresentationXml.Placeholder(element) is not null) { continue; }
                XElement? common = element.Elements().FirstOrDefault(item => item.Name.LocalName.StartsWith("nv",StringComparison.Ordinal))?.Element(p + "cNvPr");
                if (PresentationXml.Boolean(common,"hidden")) { continue; }
                switch (element.Name.LocalName)
                {
                    case "nvGrpSpPr": case "grpSpPr": case "extLst": break;
                    case "grpSp":
                        XElement group = element.Element(p + "grpSpPr") ?? throw new InvalidDataException("Missing presentation group properties.");
                        XElement gx = group.Elements().FirstOrDefault(item => item.Name.LocalName == "xfrm") ?? throw new InvalidDataException("Missing presentation group transform.");
                        await TreeAsync(part, element, PresentationGeometry.GroupTransform(gx) * parent, skipPlaceholders); break;
                    case "sp": case "cxnSp": case "pic": case "graphicFrame":
                        IReadOnlyList<XElement> chain = part == scene.Slide ? scene.ShapeChain(element) : new[] { element };
                        XElement? xfrm = chain.Select(item => PresentationXml.Properties(item)?.Elements().FirstOrDefault(child => child.Name.LocalName == "xfrm")
                            ?? item.Element(p + "xfrm")).FirstOrDefault(item => item is not null);
                        if (xfrm is null) { throw new InvalidDataException("Presentation shape placement is missing."); }
                        (double width,double height,Matrix3x2 placed) = PresentationGeometry.Placement(xfrm);
                        if (width == 0 || height == 0)
                        {
                            if (element.Name.LocalName != "cxnSp") { continue; }
                        }
                        Matrix3x2 transform = placed * parent;
                        if (element.Name.LocalName == "pic") { await PictureAsync(part,element,width,height,transform); break; }
                        if (element.Name.LocalName == "graphicFrame") { await TableAsync(part,element,width,height,transform); break; }
                        XElement? properties = chain.Select(PresentationXml.Properties).FirstOrDefault(item => item is not null);
                        XElement? geometry = properties?.Elements().FirstOrDefault(item => item.Name.LocalName is "prstGeom" or "custGeom");
                        VectorPath path = PresentationGeometry.Path(geometry,width,height,element.Name.LocalName == "cxnSp");
                        XElement? style = element.Element(p + "style");
                        Paint(path,properties,transform,style?.Elements().FirstOrDefault(item => item.Name.LocalName == "fillRef"));
                        Stroke(path,properties,style,transform);
                        XElement? body = element.Element(p + "txBody");
                        if (body is not null) { DrawText(body, chain, width, height, transform); }
                        break;
                    default: throw new NotSupportedException("Unsupported presentation shape: " + element.Name.LocalName);
                }
            }
        }
        public void Dispose() { foreach (VideoFrame image in _images.Values) { image.Dispose(); } _images.Clear(); }
    }
}
