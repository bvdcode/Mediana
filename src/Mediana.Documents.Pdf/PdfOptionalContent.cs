// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Vadim Belov <https://belov.us>

namespace Mediana.Documents.Pdf
{
    internal class PdfOptionalContent
    {
        private readonly PdfStorage _storage;
        private readonly Dictionary<PdfValue, bool> _states = [];

        internal PdfOptionalContent(PdfStorage storage, PdfValue catalog, CancellationToken token)
        {
            _storage = storage;
            PdfValue properties = Get(catalog, "OCProperties", token);
            if (properties.Kind == PdfValueKind.Null) { return; }
            RequireDictionary(properties);
            PdfValue groups = Get(properties, "OCGs", token), configuration = Get(properties, "D", token);
            RequireDictionary(configuration);
            if (groups.Kind != PdfValueKind.Array) { throw new InvalidDataException("PDF optional content requires an OCGs array."); }
            PdfValue initial = Get(configuration, "BaseState", token);
            bool enabled = initial.Kind == PdfValueKind.Null || initial.Name() switch
            {
                "ON" or "Unchanged" => true,
                "OFF" => false,
                _ => throw new InvalidDataException("Invalid PDF optional content base state.")
            };
            foreach (PdfValue reference in groups.Items)
            {
                token.ThrowIfCancellationRequested();
                PdfValue group = storage.Resolve(reference, token);
                RequireDictionary(group);
                if (Get(group, "Type", token).Name() != "OCG" || !_states.TryAdd(group, enabled))
                {
                    throw new InvalidDataException("Invalid or duplicated PDF optional content group.");
                }
            }
            SetStates(Get(configuration, "ON", token), true, token);
            SetStates(Get(configuration, "OFF", token), false, token);
            ViewUsage(properties, configuration, token);
            HashSet<string> intents = Intents(Get(configuration, "Intent", token), "View", token);
            foreach (PdfValue group in _states.Keys.ToArray())
            {
                if (!intents.Overlaps(Intents(Get(group, "Intent", token), "View", token))) { _states[group] = true; }
            }
        }

        internal bool IsVisible(PdfValue reference, CancellationToken token, ref int operations) =>
            Visible(reference, token, 0, ref operations);

        private bool Visible(PdfValue reference, CancellationToken token, int depth, ref int operations)
        {
            token.ThrowIfCancellationRequested();
            if (depth >= _storage.Budget.Options.MaximumDepth || ++operations > _storage.Budget.Options.MaximumOperations)
            {
                throw new InvalidDataException("PDF optional content exceeds its evaluation limit.");
            }
            PdfValue value = _storage.Resolve(reference, token);
            if (value.Kind == PdfValueKind.Array) { return Expression(value, token, depth, ref operations); }
            RequireDictionary(value);
            string type = Get(value, "Type", token).Name();
            if (type == "OCG")
            {
                return _states.TryGetValue(value, out bool state) ? state
                    : throw new InvalidDataException("An optional content group is absent from the document catalog.");
            }
            if (type != "OCMD") { throw new InvalidDataException("Invalid PDF optional content membership type."); }
            PdfValue expression = Get(value, "VE", token);
            if (expression.Kind != PdfValueKind.Null) { return Visible(expression, token, depth + 1, ref operations); }
            PdfValue members = Get(value, "OCGs", token);
            if (members.Kind == PdfValueKind.Null) { return true; }
            PdfValue[] groups = members.Kind == PdfValueKind.Array ? members.Items : [members];
            bool any = false, all = true;
            foreach (PdfValue group in groups)
            {
                bool visible = Visible(group, token, depth + 1, ref operations);
                any |= visible; all &= visible;
            }
            PdfValue policy = Get(value, "P", token);
            return (policy.Kind == PdfValueKind.Null ? "AnyOn" : policy.Name()) switch
            {
                "AnyOn" => any,
                "AllOn" => all,
                "AnyOff" => !all,
                "AllOff" => !any,
                _ => throw new InvalidDataException("Invalid PDF optional content policy.")
            };
        }

        private bool Expression(PdfValue expression, CancellationToken token, int depth, ref int operations)
        {
            if (expression.Items.Length < 2) { throw new InvalidDataException("Invalid optional content expression."); }
            string operation = _storage.Resolve(expression.Items[0], token).Name();
            bool any = false, all = true;
            foreach (PdfValue member in expression.Items.Skip(1))
            {
                bool visible = Visible(member, token, depth + 1, ref operations);
                any |= visible; all &= visible;
            }
            return operation switch
            {
                "And" => all,
                "Or" => any,
                "Not" when expression.Items.Length == 2 => !any,
                _ => throw new InvalidDataException("Invalid optional content expression operator.")
            };
        }

        private void SetStates(PdfValue groups, bool enabled, CancellationToken token)
        {
            if (groups.Kind == PdfValueKind.Null) { return; }
            if (groups.Kind != PdfValueKind.Array) { throw new InvalidDataException("Optional content states require an array."); }
            foreach (PdfValue reference in groups.Items)
            {
                token.ThrowIfCancellationRequested();
                PdfValue group = _storage.Resolve(reference, token);
                if (!_states.ContainsKey(group)) { throw new InvalidDataException("Unknown PDF optional content group."); }
                _states[group] = enabled;
            }
        }

        private void ViewUsage(PdfValue properties, PdfValue configuration, CancellationToken token)
        {
            PdfValue applications = Get(configuration, "AS", token);
            if (applications.Kind == PdfValueKind.Null) { return; }
            if (applications.Kind != PdfValueKind.Array) { throw new InvalidDataException("Optional content usage requires an array."); }
            foreach (PdfValue reference in applications.Items)
            {
                token.ThrowIfCancellationRequested();
                PdfValue application = _storage.Resolve(reference, token);
                RequireDictionary(application);
                if (Get(application, "Event", token).Name() != "View") { continue; }
                HashSet<string> categories = Intents(Get(application, "Category", token), string.Empty, token);
                if (categories.Any(category => category != "View")) { throw new NotSupportedException("PDF optional content usage category requires a viewing context."); }
                if (!categories.Contains("View")) { continue; }
                PdfValue groups = Get(application, "OCGs", token);
                if (groups.Kind == PdfValueKind.Null) { groups = Get(properties, "OCGs", token); }
                if (groups.Kind != PdfValueKind.Array) { throw new InvalidDataException("Optional content usage groups require an array."); }
                foreach (PdfValue member in groups.Items)
                {
                    PdfValue group = _storage.Resolve(member, token);
                    if (!_states.ContainsKey(group)) { throw new InvalidDataException("Unknown PDF optional content usage group."); }
                    PdfValue view = Get(Get(group, "Usage", token), "View", token);
                    PdfValue state = Get(view, "ViewState", token);
                    if (state.Kind == PdfValueKind.Null) { continue; }
                    _states[group] = state.Name() switch
                    {
                        "ON" => true,
                        "OFF" => false,
                        _ => throw new InvalidDataException("Invalid optional content view state.")
                    };
                }
            }
        }

        private HashSet<string> Intents(PdfValue value, string initial, CancellationToken token)
        {
            if (value.Kind == PdfValueKind.Null) { return initial.Length == 0 ? [] : [initial]; }
            if (value.Kind == PdfValueKind.Name) { return [value.Name()]; }
            if (value.Kind != PdfValueKind.Array) { throw new InvalidDataException("PDF optional content intents require names."); }
            return value.Items.Select(item => _storage.Resolve(item, token).Name()).ToHashSet(StringComparer.Ordinal);
        }
        private PdfValue Get(PdfValue value, string name, CancellationToken token) => _storage.Resolve(value.Get(name), token);
        private static void RequireDictionary(PdfValue value)
        {
            if (value.Kind != PdfValueKind.Dictionary) { throw new InvalidDataException("PDF optional content requires a dictionary."); }
        }
    }
}
