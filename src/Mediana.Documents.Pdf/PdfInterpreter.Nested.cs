// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using Mediana.Drawing;
using System.Numerics;

namespace Mediana.Documents.Pdf
{
    internal partial class PdfInterpreter
    {
        private void ExecuteNested(PdfObject obj, PdfValue resources, Matrix3x2 transform, int depth,
            bool glyph = false, PdfValue? boundingBox = null)
        {
            PdfContentState saved = new(_state, _path, _current, _start, _hasPoint, _pendingClip,
                _text, _textLine, _inText, _textClip, _hasTextClip);
            try
            {
                _commands.Add(new StateCommand(true));
                _state = _state with { Transform = transform };
                if (boundingBox is not null)
                {
                    double[] box = PdfDocument.Box(boundingBox);
                    VectorPath bounds = new VectorPath().Rectangle(Float(box[0]), Float(box[1]),
                        Float(box[2] - box[0]), Float(box[3] - box[1])).Transform(transform);
                    _commands.Add(new ClipCommand(bounds, FillRule.NonZero));
                }
                _path = new();
                _hasPoint = false;
                _pendingClip = null;
                _inText = false;
                _hasTextClip = false;
                _textClip = new();
                Execute(storage.Decode(obj, token), resources, depth, glyph);
                _commands.Add(new StateCommand(false));
            }
            finally
            {
                _state = saved.Graphics;
                _path = saved.Path;
                _current = saved.Current;
                _start = saved.Start;
                _hasPoint = saved.HasPoint;
                _pendingClip = saved.PendingClip;
                _text = saved.Text;
                _textLine = saved.TextLine;
                _inText = saved.InText;
                _textClip = saved.TextClip;
                _hasTextClip = saved.HasTextClip;
            }
        }
    }
}
