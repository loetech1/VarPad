using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VarPad
{
    internal static class InputGrouping
    {
        // A comma-shaped grouping character distinct from the ASCII argument comma.
        // It occupies a normal character cell; exports and saves strip it.
        internal const char Mark = '\u201a';
        internal static string Plain(string text) { return text.Replace(Mark.ToString(), ""); }
        internal static string Display(string text)
        {
            string plain = Plain(text);
            var insert = new List<int>();
            int start = 0;
            foreach (string line in plain.Split('\n'))
            {
                insert.AddRange(Separators(line).Select(i => start + i));
                start += line.Length + 1;
            }
            foreach (int i in insert.OrderByDescending(i => i)) plain = plain.Insert(i, Mark.ToString());
            return plain;
        }
        internal static int MapPosition(string original, int index, string display)
        {
            int raw = Plain(original.Substring(0, Math.Min(index, original.Length))).Length;
            int p = 0, seen = 0;
            while (p < display.Length && seen < raw) { if (display[p] != Mark) seen++; p++; }
            if (p < display.Length && display[p] == Mark) p++;
            return p;
        }
        private static readonly Regex Number = new Regex(@"\G(?:0[xX][0-9a-fA-F]*|0[bB][01]*|(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?)");
        internal static IEnumerable<int> Separators(string source)
        {
            bool quoted = false;
            for (int i = 0; i < source.Length; )
            {
                char c = source[i];
                if (c == '#' || c == '/' && i + 1 < source.Length && source[i + 1] == '/') yield break;
                if (c == '"' || c == '\u201c' || c == '\u201d') { quoted = !quoted; i++; continue; }
                if (quoted) { i++; continue; }
                if (Char.IsLetter(c) || c == '_')
                {
                    do { i++; } while (i < source.Length && (Char.IsLetterOrDigit(source[i]) || source[i] == '_'));
                    continue;
                }
                Match number = Number.Match(source, i);
                if (!number.Success) { i++; continue; }
                string token = number.Value;
                if (!token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && !token.StartsWith("0b", StringComparison.OrdinalIgnoreCase) && token.IndexOfAny(new[] { 'e', 'E' }) < 0)
                {
                    int length = token.IndexOf('.'); if (length < 0) length = token.Length;
                    for (int digit = length - 3; digit > 0; digit -= 3) yield return i + digit;
                }
                i += token.Length;
            }
        }
    }
}
