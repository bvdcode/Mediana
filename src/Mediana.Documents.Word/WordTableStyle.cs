// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal class WordTableStyle
    {
        private readonly WordReadContext _context;
        private readonly IReadOnlyList<XElement> _chain;
        private readonly XElement? _direct;
        private readonly XElement? _look;
        private readonly int _flags;
        private XNamespace W => _context.W;
        internal XElement Properties { get; }

        internal WordTableStyle(WordReadContext context, IReadOnlyList<XElement> chain, XElement? direct)
        {
            _context = context; _chain = chain; _direct = direct;
            XElement whole = Formatting();
            Apply(whole, "wholeTable");
            Properties = new(W + "tblPr");
            WordStyles.Merge(Properties, whole.Element(W + "tblPr"));
            WordStyles.Merge(Properties, direct);
            _look = Properties.Element(W + "tblLook");
            string? flags = context.Value(_look);
            if (flags is not null && !int.TryParse(flags, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _flags))
            { throw new InvalidDataException("Invalid Word table look flags."); }
        }

        internal XElement Cell(int row, int rows, int column, int span, int columns)
        {
            XElement result = Formatting();
            Apply(result, "wholeTable");
            bool firstRow = Enabled("firstRow", 0x20) && row == 0;
            bool lastRow = Enabled("lastRow", 0x40) && row == rows - 1;
            bool firstColumn = Enabled("firstColumn", 0x80) && column == 0;
            bool lastColumn = Enabled("lastColumn", 0x100) && column + span == columns;
            if (!Enabled("noVBand", 0x400) && !firstColumn && !lastColumn)
            {
                int index = column - (Enabled("firstColumn", 0x80) ? 1 : 0);
                Apply(result, index / BandSize("tblStyleColBandSize") % 2 == 0 ? "band1Vert" : "band2Vert");
            }
            if (!Enabled("noHBand", 0x200) && !firstRow && !lastRow)
            {
                int index = row - (Enabled("firstRow", 0x20) ? 1 : 0);
                Apply(result, index / BandSize("tblStyleRowBandSize") % 2 == 0 ? "band1Horz" : "band2Horz");
            }
            if (firstRow) { Apply(result, "firstRow"); }
            if (lastRow) { Apply(result, "lastRow"); }
            if (firstColumn) { Apply(result, "firstCol"); }
            if (lastColumn) { Apply(result, "lastCol"); }
            if (firstRow && firstColumn) { Apply(result, "nwCell"); }
            if (firstRow && lastColumn) { Apply(result, "neCell"); }
            if (lastRow && firstColumn) { Apply(result, "swCell"); }
            if (lastRow && lastColumn) { Apply(result, "seCell"); }
            XElement table = result.Element(W + "tblPr") ?? new(W + "tblPr");
            if (table.Parent is null) { result.Add(table); }
            WordStyles.Merge(table, _direct);
            return result;
        }

        private bool Enabled(string name, int mask) => _context.Attribute(_look, name) switch
        {
            null => (_flags & mask) != 0,
            "1" or "true" or "on" => true,
            "0" or "false" or "off" => false,
            _ => throw new InvalidDataException("Invalid Word table look boolean.")
        };

        private int BandSize(string name)
        {
            int size = WordReadContext.Integer(_context.Value(Properties.Element(W + name)) ?? "1");
            return size > 0 ? size : throw new InvalidDataException("Word table band size must be positive.");
        }

        private XElement Formatting()
        {
            XElement result = new(W + "style");
            foreach (XElement style in _chain) { Merge(result, style); }
            return result;
        }

        private void Apply(XElement result, string type)
        {
            foreach (XElement style in _chain)
            {
                foreach (XElement conditional in style.Elements(W + "tblStylePr"))
                {
                    if (_context.Attribute(conditional, "type") == type) { Merge(result, conditional); }
                }
            }
        }

        private void Merge(XElement destination, XElement source)
        {
            foreach (string name in new[] { "tblPr", "trPr", "tcPr", "pPr", "rPr" })
            {
                XElement target = destination.Element(W + name) ?? new(W + name);
                if (target.Parent is null) { destination.Add(target); }
                WordStyles.Merge(target, source.Element(W + name));
            }
        }
    }
}
