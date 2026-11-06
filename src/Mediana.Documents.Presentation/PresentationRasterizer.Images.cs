// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Codecs;
using Mediana.Core.Video;
using Mediana.Drawing;
using Mediana.Imaging;
using System.Numerics;
using System.Xml.Linq;
namespace Mediana.Documents.Presentation
{
    internal partial class PresentationRasterizer
    {
        private async ValueTask PictureAsync(PresentationPart part,XElement picture,double width,double height,Matrix3x2 transform)
        {
            XElement fill = picture.Elements().FirstOrDefault(item => item.Name.LocalName == "blipFill") ?? throw new InvalidDataException("Presentation picture has no image fill.");
            XNamespace a = PresentationXml.Drawing(picture);
            XElement blip = fill.Element(a+"blip") ?? throw new InvalidDataException("Presentation picture has no image relationship.");
            string imagePart = part.Relationship(PresentationXml.RelationId(blip,"embed"),"image");
            if (!_images.TryGetValue(imagePart,out VideoFrame? image))
            {
                double sx = Math.Sqrt(transform.M11*transform.M11+transform.M12*transform.M12);
                double sy = Math.Sqrt(transform.M21*transform.M21+transform.M22*transform.M22);
                ImageDecodeRequest request = new() { MaximumPixels = options.MaximumImagePixels,
                    PreferredWidth = (int)Math.Clamp(Math.Ceiling(width*sx),1,int.MaxValue),
                    PreferredHeight = (int)Math.Clamp(Math.Ceiling(height*sy),1,int.MaxValue), WarningHandler = options.WarningHandler };
                image = await ImageResources.DecodeAsync(document.Package.ReadPart(imagePart,token),options.Images,request,allocator,options.ImageColorResolver,token).ConfigureAwait(false);
                bool stored = false;
                try { _images.Add(imagePart,image); stored = true; }
                finally { if (!stored) { image.Dispose(); } }
            }
            if (fill.Element(a+"tile") is not null) { throw new NotSupportedException("Tiled presentation pictures are not supported."); }
            XElement? crop = fill.Element(a+"srcRect");
            double left = PresentationXml.Number(crop,"l")/100000, right = PresentationXml.Number(crop,"r")/100000;
            double top = PresentationXml.Number(crop,"t")/100000, bottom = PresentationXml.Number(crop,"b")/100000;
            if (1-left-right <= 0 || 1-top-bottom <= 0) { throw new InvalidDataException("Invalid presentation picture crop."); }
            XElement? target = fill.Element(a+"stretch")?.Element(a+"fillRect");
            double dl = PresentationXml.Number(target,"l")/100000, dr = PresentationXml.Number(target,"r")/100000;
            double dt = PresentationXml.Number(target,"t")/100000, db = PresentationXml.Number(target,"b")/100000;
            double iw = width*(1-dl-dr)/(1-left-right), ih = height*(1-dt-db)/(1-top-bottom);
            Matrix3x2 placed = Matrix3x2.CreateScale((float)(iw/image.Format.Width),(float)(ih/image.Format.Height))
                * Matrix3x2.CreateTranslation((float)(width*dl-iw*left),(float)(height*dt-ih*top)) * transform;
            canvas.Save();
            try
            {
                VectorPath clip = PresentationGeometry.Path(PresentationXml.Properties(picture)?.Element(a+"prstGeom"),width,height,false);
                canvas.ClipPath(clip,transform,cancellationToken:token);
                canvas.DrawImage(image,placed,cancellationToken:token);
            }
            finally { canvas.Restore(); }
        }
        private ValueTask TableAsync(PresentationPart part,XElement frame,double width,double height,Matrix3x2 transform)
        {
            XNamespace a = PresentationXml.Drawing(frame);
            XElement data = frame.Element(a+"graphic")?.Element(a+"graphicData") ?? throw new InvalidDataException("Empty presentation graphic frame.");
            XElement table = data.Element(a+"tbl") ?? throw new NotSupportedException("Only tables are supported in presentation graphic frames.");
            if (table.Element(a+"tblPr")?.Element(a+"tableStyleId") is not null)
            { throw new NotSupportedException("Presentation table theme styles require a table style definition."); }
            double[] columns = (table.Element(a+"tblGrid")?.Elements(a+"gridCol") ?? []).Select(item => PresentationXml.Points(item,"w")).ToArray();
            double y = 0;
            foreach (XElement row in table.Elements(a+"tr"))
            {
                token.ThrowIfCancellationRequested();
                double rowHeight = PresentationXml.Points(row,"h"), x = 0;
                int column = 0;
                foreach (XElement cell in row.Elements(a+"tc"))
                {
                    if (column >= columns.Length) { throw new InvalidDataException("Presentation table row exceeds its grid."); }
                    int span = checked((int)PresentationXml.Number(cell,"gridSpan",1));
                    if (span < 1 || span > columns.Length-column) { throw new InvalidDataException("Invalid presentation table cell span."); }
                    double cellWidth = columns.AsSpan(column,span).ToArray().Sum();
                    Matrix3x2 cellTransform = Matrix3x2.CreateTranslation((float)x,(float)y)*transform;
                    Paint(new VectorPath().Rectangle(0,0,(float)cellWidth,(float)rowHeight),cell.Element(a+"tcPr"),cellTransform);
                    if (cell.Element(a+"txBody") is XElement text) { DrawText(text,[],cellWidth,rowHeight,cellTransform); }
                    x += columns[column++];
                }
                y += rowHeight;
            }
            return ValueTask.CompletedTask;
        }
    }
}
