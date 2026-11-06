// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

using System.Text;
namespace Mediana.Documents.Pdf
{
    internal class PdfDocumentText(PdfStorage storage,PdfPageSource[] pages,PdfOptionalContent optionalContent,DocumentTextOptions options,CancellationToken token)
    {
        private readonly DocumentTextBudget _budget = new(options);
        private readonly Dictionary<int,IReadOnlyList<PdfTextRun>> _runs = [];
        private readonly HashSet<PdfTextRun> _used = [];
        private readonly HashSet<PdfValue> _active = [];
        private PdfValue _roles = PdfValue.Null;
        private int _nodes;
        internal IEnumerable<DocumentTextBlock> Read()
        {
            PdfValue catalog = Resolve(storage.Trailer.Get("Root"));
            PdfValue structure = Get(catalog,"StructTreeRoot");
            if (structure.Kind != PdfValueKind.Null)
            {
                _roles = Get(structure,"RoleMap");
                foreach (DocumentTextBlock block in Tagged(structure.Get("K"),PdfValue.Null,[],0,0)) { yield return block; }
            }
            for (int page = options.StartPartIndex; page < pages.Length && options.IncludesPart(page); page++)
            {
                IReadOnlyList<PdfTextRun> all = Runs(page);
                List<PdfTextRun> ordered = all.Where(item => !_used.Contains(item)).OrderByDescending(item => item.Position.Y).ThenBy(item => item.Position.X).ThenBy(item => item.Sequence).ToList();
                int index = 0, cursor = 0;
                while (cursor < ordered.Count)
                {
                    token.ThrowIfCancellationRequested();
                    PdfTextRun first = ordered[cursor];
                    double tolerance = Math.Max(.5,first.Size*.25);
                    int end = cursor+1;
                    while (end < ordered.Count && Math.Abs(ordered[end].Position.Y-first.Position.Y) <= tolerance) { end++; }
                    string text = string.Concat(ordered.GetRange(cursor,end-cursor).OrderBy(item => item.Position.X).ThenBy(item => item.Sequence).Select(item => item.Text));
                    cursor = end;
                    _budget.Consume(text,token);
                    yield return new(text,page,null,Array.AsReadOnly(new[] { new DocumentTextPathSegment(DocumentTextNodeKind.Page,page),new DocumentTextPathSegment(DocumentTextNodeKind.Paragraph,index++) }),DocumentTextStructure.Reconstructed);
                }
            }
        }
        private IEnumerable<DocumentTextBlock> Tagged(PdfValue reference,PdfValue inheritedPage,IReadOnlyList<DocumentTextPathSegment> path,int index,int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth >= storage.Budget.Options.MaximumDepth || ++_nodes > storage.Budget.Options.MaximumOperations) { throw new InvalidDataException("PDF text structure exceeds its limit."); }
            PdfValue node = Resolve(reference);
            if (node.Kind == PdfValueKind.Null) { yield break; }
            if (node.Kind == PdfValueKind.Array)
            {
                for (int i = 0; i < node.Items.Length; i++)
                { foreach (DocumentTextBlock block in Tagged(node.Items[i],inheritedPage,path,i,depth+1)) { yield return block; } }
                yield break;
            }
            if (node.Kind == PdfValueKind.Number)
            {
                DocumentTextBlock? block = Marked(inheritedPage,inheritedPage,node.Integer(),path);
                if (block is not null) { yield return block; }
                yield break;
            }
            if (node.Kind != PdfValueKind.Dictionary || !_active.Add(node)) { throw new InvalidDataException("Invalid or cyclic PDF text structure."); }
            try
            {
                PdfValue page = node.Get("Pg").Kind == PdfValueKind.Null ? inheritedPage : Get(node,"Pg");
                string type = node.Get("Type").Kind == PdfValueKind.Null ? "StructElem" : Get(node,"Type").Name();
                if (type == "MCR")
                {
                    PdfValue stream = node.Get("Stm").Kind == PdfValueKind.Null ? page : Get(node,"Stm");
                    DocumentTextBlock? block = Marked(page,stream,Get(node,"MCID").Integer(),path);
                    if (block is not null) { yield return block; }
                    yield break;
                }
                if (type == "OBJR") { yield break; }
                if (type != "StructElem") { throw new InvalidDataException("Unknown PDF text structure node."); }
                string role = Get(node,"S").Name();
                HashSet<string> visited = new(StringComparer.Ordinal);
                while (_roles.Get(role).Kind != PdfValueKind.Null)
                {
                    if (!visited.Add(role) || visited.Count > storage.Budget.Options.MaximumDepth) { throw new InvalidDataException("Cyclic PDF structure role mapping."); }
                    role = Get(_roles,role).Name();
                }
                DocumentTextPathSegment[] nested = path.Append(new DocumentTextPathSegment(Kind(role),index)).ToArray();
                if (node.Get("ActualText").Kind != PdfValueKind.Null)
                {
                    int part = PageIndex(page);
                    if (options.IncludesPart(part))
                    {
                        MarkReplaced(node.Get("K"),page,depth+1);
                        string text = PdfTextStrings.Decode(Get(node,"ActualText"));
                        _budget.Consume(text,token);
                        yield return new(text,part,null,Array.AsReadOnly(nested),DocumentTextStructure.Declared);
                    }
                    yield break;
                }
                foreach (DocumentTextBlock block in Tagged(node.Get("K"),page,nested,0,depth+1)) { yield return block; }
            }
            finally { _active.Remove(node); }
        }
        private void MarkReplaced(PdfValue reference,PdfValue inheritedPage,int depth)
        {
            token.ThrowIfCancellationRequested();
            if (depth >= storage.Budget.Options.MaximumDepth || ++_nodes > storage.Budget.Options.MaximumOperations) { throw new InvalidDataException("Excessive PDF replacement structure."); }
            PdfValue node = Resolve(reference);
            if (node.Kind == PdfValueKind.Null) { return; }
            if (node.Kind == PdfValueKind.Array) { foreach (PdfValue child in node.Items) { MarkReplaced(child,inheritedPage,depth+1); } return; }
            PdfValue page = node.Get("Pg").Kind == PdfValueKind.Null ? inheritedPage : Get(node,"Pg");
            if (node.Kind == PdfValueKind.Number || node.Get("MCID").Kind != PdfValueKind.Null)
            {
                int index = PageIndex(page);
                if (!options.IncludesPart(index)) { return; }
                int mcid = node.Kind == PdfValueKind.Number ? node.Integer() : Get(node,"MCID").Integer();
                PdfValue stream = node.Get("Stm").Kind == PdfValueKind.Null ? page : Get(node,"Stm");
                foreach (PdfTextRun run in Runs(index)) { if (run.Mcid == mcid && ReferenceEquals(run.Stream,stream)) { _used.Add(run); } }
                return;
            }
            MarkReplaced(node.Get("K"),page,depth+1);
        }
        private DocumentTextBlock? Marked(PdfValue page,PdfValue stream,int mcid,IReadOnlyList<DocumentTextPathSegment> path)
        {
            if (mcid < 0) { throw new InvalidDataException("Negative PDF marked-content identifier."); }
            int index = PageIndex(page);
            if (!options.IncludesPart(index)) { return null; }
            PdfTextRun[] matches = Runs(index).Where(item => ReferenceEquals(item.Stream,stream) && item.Mcid == mcid).ToArray();
            if (matches.Length == 0) { return null; }
            foreach (PdfTextRun run in matches) { _used.Add(run); }
            string text = string.Concat(matches.Select(item => item.Text));
            _budget.Consume(text,token);
            return new(text,index,null,Array.AsReadOnly(path.ToArray()),DocumentTextStructure.Declared);
        }
        private IReadOnlyList<PdfTextRun> Runs(int index)
        {
            if (!_runs.TryGetValue(index,out IReadOnlyList<PdfTextRun>? result))
            { result = new PdfTextCollector(storage,optionalContent,options.MaximumCharacters,token).Read(pages[index]); _runs.Add(index,result); }
            return result;
        }
        private int PageIndex(PdfValue page)
        {
            for (int i = 0; i < pages.Length; i++) { if (ReferenceEquals(pages[i].Dictionary,page)) { return i; } }
            throw new InvalidDataException("PDF structure references a page outside its page tree.");
        }
        private PdfValue Resolve(PdfValue value) => storage.Resolve(value,token);
        private PdfValue Get(PdfValue value,string name) => Resolve(value.Get(name));
        private static DocumentTextNodeKind Kind(string role) => role switch
        {
            "Document" => DocumentTextNodeKind.Document,"Part" or "Sect" or "Div" or "Art" => DocumentTextNodeKind.Section,
            "H" or "H1" or "H2" or "H3" or "H4" or "H5" or "H6" => DocumentTextNodeKind.Heading,
            "L" or "LI" or "LBody" or "Lbl" => DocumentTextNodeKind.ListItem,"Table" => DocumentTextNodeKind.Table,"TR" => DocumentTextNodeKind.Row,"TD" or "TH" => DocumentTextNodeKind.Cell,
            "P" or "Span" or "Quote" or "Note" or "Reference" or "BibEntry" or "Code" or "Link" or "Annot" or "Figure" or "Formula" or "Form" or "Caption" or "TOC" or "TOCI" or "Index" or "NonStruct" or "Private" => DocumentTextNodeKind.Paragraph,
            _ => throw new NotSupportedException("Unknown PDF structure role: " + role)
        };
    }
}
