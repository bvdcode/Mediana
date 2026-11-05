// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Mediana.Documents.Word
{
    internal class WordNumbering
    {
        private readonly WordReadContext _context;
        private readonly WordFormatReader _formats;
        private readonly Dictionary<int, XElement> _definitions;
        private readonly Dictionary<int, XElement> _instances;
        private readonly Dictionary<int, int[]> _counters = [];
        private XNamespace W => _context.W;

        internal WordNumbering(WordReadContext context, string main)
        {
            _context = context;
            _formats = new(context);
            XElement? root = context.RelatedXml(main, "numbering");
            _definitions = root?.Elements(W + "abstractNum").ToDictionary(element => Id(element, "abstractNumId")) ?? [];
            _instances = root?.Elements(W + "num").ToDictionary(element => Id(element, "numId")) ?? [];
        }

        internal IReadOnlyList<WordInline> Prefix(XElement properties, WordRunFormat format)
        {
            XElement? num = properties.Element(W + "numPr");
            if (_context.Value(num?.Element(W + "numId")) is not string text) { return Array.Empty<WordInline>(); }
            int id = WordReadContext.Integer(text);
            if (id == 0) { return Array.Empty<WordInline>(); }
            int level = WordReadContext.Integer(_context.Value(num?.Element(W + "ilvl")) ?? "0");
            if (level is < 0 or > 8) { throw new InvalidDataException("Invalid Word numbering level."); }
            XElement definition = Level(id, level);
            XElement inherited = new(W + "pPr");
            WordStyles.Merge(inherited, definition.Element(W + "pPr"));
            WordStyles.Merge(inherited, properties);
            properties.ReplaceAttributes(inherited.Attributes());
            properties.ReplaceNodes(inherited.Nodes());
            if (!_counters.TryGetValue(id, out int[]? counters))
            {
                counters = new int[9];
                Array.Fill(counters, int.MinValue);
                _counters.Add(id, counters);
            }
            counters[level] = counters[level] == int.MinValue ? Start(id, level) : checked(counters[level] + 1);
            for (int deeper = level + 1; deeper < counters.Length; deeper++)
            {
                XElement? next = OptionalLevel(id, deeper);
                int restart = WordReadContext.Integer(_context.Value(next?.Element(W + "lvlRestart")) ?? deeper.ToString(CultureInfo.InvariantCulture));
                if (restart != 0 && restart - 1 <= level) { counters[deeper] = int.MinValue; }
            }
            string pattern = _context.Value(definition.Element(W + "lvlText")) ?? "";
            StringBuilder label = new();
            for (int i = 0; i < pattern.Length; i++)
            {
                if (pattern[i] == '%' && i + 1 < pattern.Length && pattern[i + 1] is >= '1' and <= '9')
                {
                    int index = pattern[++i] - '1';
                    XElement referenced = Level(id, index);
                    int value = counters[index] == int.MinValue ? Start(id, index) : counters[index];
                    label.Append(Format(value, _context.Value(referenced.Element(W + "numFmt")) ?? "decimal"));
                }
                else { label.Append(pattern[i]); }
            }
            WordRunFormat labelFormat = _formats.Run(format, definition.Element(W + "rPr"), false);
            List<WordInline> prefix = [new WordText(label.ToString(), labelFormat)];
            switch (_context.Value(definition.Element(W + "suff")))
            {
                case null or "tab": prefix.Add(new WordTab(format)); break;
                case "space": prefix.Add(new WordText(" ", format)); break;
                case "nothing": break;
                default: throw new InvalidDataException("Invalid Word numbering suffix.");
            }
            return prefix;
        }

        private int Id(XElement element, string name) => WordReadContext.Integer(_context.Attribute(element, name)
            ?? throw new InvalidDataException("Missing Word numbering identifier."));

        private XElement Instance(int id) => _instances.TryGetValue(id, out XElement? value)
            ? value : throw new InvalidDataException("Missing Word numbering instance.");

        private XElement? OptionalLevel(int id, int level)
        {
            XElement instance = Instance(id);
            XElement? overrides = instance.Elements(W + "lvlOverride").SingleOrDefault(item => Id(item, "ilvl") == level);
            if (overrides?.Element(W + "lvl") is XElement replacement) { return replacement; }
            int abstractId = WordReadContext.Integer(_context.Value(instance.Element(W + "abstractNumId"))
                ?? throw new InvalidDataException("Missing Word abstract numbering reference."));
            if (!_definitions.TryGetValue(abstractId, out XElement? definition)) { throw new InvalidDataException("Missing Word numbering definition."); }
            return definition.Elements(W + "lvl").SingleOrDefault(item => Id(item, "ilvl") == level);
        }

        private XElement Level(int id, int level) => OptionalLevel(id, level) ?? throw new InvalidDataException("Missing Word numbering level.");
        private int Start(int id, int level)
        {
            XElement? overrides = Instance(id).Elements(W + "lvlOverride").SingleOrDefault(item => Id(item, "ilvl") == level);
            return WordReadContext.Integer(_context.Value(overrides?.Element(W + "startOverride"))
                ?? _context.Value(Level(id, level).Element(W + "start")) ?? "1");
        }

        private static string Format(int value, string format) => format switch
        {
            "decimal" => value.ToString(CultureInfo.InvariantCulture),
            "decimalZero" => value.ToString("D2", CultureInfo.InvariantCulture),
            "lowerLetter" => Letters(value).ToLowerInvariant(),
            "upperLetter" => Letters(value),
            "lowerRoman" => Roman(value).ToLowerInvariant(),
            "upperRoman" => Roman(value),
            "none" => "",
            _ => throw new NotSupportedException("Unsupported Word numbering format: " + format)
        };

        private static string Letters(int value)
        {
            if (value <= 0) { throw new InvalidDataException("Alphabetic numbering must be positive."); }
            StringBuilder result = new();
            while (value > 0) { value--; result.Insert(0, (char)('A' + value % 26)); value /= 26; }
            return result.ToString();
        }

        private static string Roman(int value)
        {
            if (value is < 1 or > 3999) { throw new NotSupportedException("Roman numbering must be in the range 1 through 3999."); }
            ReadOnlySpan<int> values = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
            string[] symbols = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];
            StringBuilder result = new();
            for (int i = 0; i < values.Length; i++)
            {
                while (value >= values[i]) { result.Append(symbols[i]); value -= values[i]; }
            }
            return result.ToString();
        }
    }
}
