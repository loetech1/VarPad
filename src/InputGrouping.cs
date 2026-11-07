using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VarPad
{
    // Positions refer to the original source, never a rewritten expression.
    internal static class InputGrouping
    {
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
                if (!token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
                    !token.StartsWith("0b", StringComparison.OrdinalIgnoreCase) && token.IndexOfAny(new[] { 'e', 'E' }) < 0)
                {
                    int integerLength = token.IndexOf('.');
                    if (integerLength < 0) integerLength = token.Length;
                    for (int digit = integerLength - 3; digit > 0; digit -= 3) yield return i + digit;
                }
                i += token.Length;
            }
        }
    }
}
