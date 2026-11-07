using System;
using System.Collections.Generic;
using System.Linq;

namespace VarPad
{
    internal sealed class CompletionItem
    {
        internal string Name;
        internal string Kind;
        internal bool Function;
        public string Label { get { return Name + (Function ? "()" : "") + "   · " + Kind; } }
    }

    internal sealed class CompletionMatch
    {
        internal int Start, Length;
        internal readonly List<CompletionItem> Items = new List<CompletionItem>();
    }

    internal static class Completion
    {
        private static bool Letter(char c) { return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c == '_'; }
        private static bool NameChar(char c) { return Letter(c) || c >= '0' && c <= '9'; }

        internal static CompletionMatch Find(string text, int caret, bool degrees, bool explicitRequest)
        {
            if (caret < 0 || caret > text.Length) return null;
            int line = caret == 0 ? 0 : text.LastIndexOfAny(new[] { '\r', '\n' }, caret - 1) + 1;
            string before = text.Substring(line, caret - line).Replace('\u201c', '"').Replace('\u201d', '"');
            if (before.Contains("#") || before.Contains("//") || before.Count(c => c == '"') % 2 != 0) return null;
            int start = caret;
            while (start > line && NameChar(text[start - 1])) start--;
            string prefix = text.Substring(start, caret - start);
            if (prefix.Length == 0 && !explicitRequest || prefix.Length > 0 && !Letter(prefix[0])) return null;
            int end = caret;
            while (end < text.Length && NameChar(text[end])) end++;
            // Do not overwrite a name being declared on the left of '='.
            int next = end;
            while (next < text.Length && (text[next] == ' ' || text[next] == '\t')) next++;
            if (text.Substring(line, start - line).Trim().Length == 0 && next < text.Length && text[next] == '=') return null;

            CompletionMatch match = new CompletionMatch { Start = start, Length = end - start };
            // Evaluate only what is in scope before the cursor. A valid leading
            // fragment also makes quoted bindings on this same line available.
            string scope = text.Substring(0, start).TrimEnd(' ', '\t', '+', '-', '*', '/', '^', '%', '&', '|', '<', '>', ',', '(');
            SheetResult available = Calculator.EvaluateSheet(scope, degrees);
            foreach (string name in available.Variables.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                Add(match, name, "variable", false, prefix);
            foreach (string name in new[] { "ans", "e", "phi", "pi", "tau" })
                Add(match, name, name == "ans" ? "previous answer" : "constant", false, prefix);
            foreach (string name in Calculator.FunctionNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                Add(match, name, "function", true, prefix);
            return match.Items.Count == 0 ? null : match;
        }

        private static void Add(CompletionMatch match, string name, string kind, bool function, string prefix)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                match.Items.Add(new CompletionItem { Name = name, Kind = kind, Function = function });
        }

        internal static string Insertion(string text, CompletionMatch match, CompletionItem item, out int caretOffset)
        {
            int next = match.Start + match.Length;
            while (next < text.Length && (text[next] == ' ' || text[next] == '\t')) next++;
            bool brackets = item.Function && (next >= text.Length || text[next] != '(');
            caretOffset = item.Name.Length + (brackets ? 1 : 0);
            return item.Name + (brackets ? "()" : "");
        }
    }
}
