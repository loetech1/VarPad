using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace VarPad
{
    public sealed class CalculationException : Exception
    {
        public CalculationException(string message) : base(message) { }
    }

    public sealed class LineResult
    {
        public string Source = "";
        public string Variable;
        public string AssignedExpression;
        public double? Value;
        public string Error;
        public bool IsMemo;
        public bool? Matches;
        public string Display
        {
            get { return Error != null ? Error : Value.HasValue ? "= " + Calculator.Format(Value.Value) : ""; }
        }
    }

    public sealed class SheetResult
    {
        public readonly List<LineResult> Lines = new List<LineResult>();
        public readonly Dictionary<string, double> Variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    // A bounded, explicit expression parser. It never executes scripts or arbitrary code.
    public static class Calculator
    {
        public const int MaxLineLength = 4096;
        internal static IEnumerable<string> FunctionNames { get { return Parser.FunctionNames; } }
        private static readonly Regex Assignment = new Regex(@"^([a-zA-Z_][a-zA-Z_0-9]*)\s*=(?!=)(.*)$");
        private static readonly Regex QuotedAssignment = new Regex(@"^(.+?)\s*""([a-zA-Z_][a-zA-Z_0-9]*)""\s*$");
        private static readonly Regex QuotedName = new Regex(@"""([a-zA-Z_][a-zA-Z_0-9]*)""");
        private static readonly Regex ValidName = new Regex(@"^[a-zA-Z_][a-zA-Z_0-9]*$");
        private static readonly HashSet<string> Reserved = new HashSet<string>(new[] { "pi", "e", "phi", "tau", "ans", "mod", "xor", "and", "or", "not" }, StringComparer.OrdinalIgnoreCase);

        public static string Format(double value)
        {
            string text = FormatInput(value);
            // Keep scientific notation and the existing significant-digit
            // rounding. Group only the integer part of ordinary decimals.
            if (text.IndexOfAny(new[] { 'e', 'E' }) >= 0) return text;
            int first = text.StartsWith("-", StringComparison.Ordinal) ? 1 : 0;
            int end = text.IndexOf('.');
            if (end < 0) end = text.Length;
            for (int position = end - 3; position > first; position -= 3)
                text = text.Insert(position, ",");
            return text;
        }

        public static string FormatInput(double value)
        {
            if (Math.Abs(value) < 1e-14 && value != 0) return value.ToString("G12", CultureInfo.InvariantCulture);
            return (value == 0 ? 0 : value).ToString("G12", CultureInfo.InvariantCulture);
        }

        public static SheetResult EvaluateSheet(string text, bool degrees)
        {
            SheetResult sheet = new SheetResult();
            double? previous = null;
            foreach (string source in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                LineResult line = new LineResult { Source = source };
                sheet.Lines.Add(line);
                string expression = source.Trim().Replace('\u201c', '"').Replace('\u201d', '"');
                int comment = expression.IndexOf('#');
                int slashComment = expression.IndexOf("//", StringComparison.Ordinal);
                if (slashComment >= 0 && (comment < 0 || slashComment < comment)) comment = slashComment;
                if (comment >= 0) expression = expression.Substring(0, comment).Trim();
                if (expression.Length == 0 || expression == "?") { line.IsMemo = true; continue; }
                Match assignment = Assignment.Match(expression);
                Match quoted = QuotedAssignment.Match(expression);
                if (assignment.Success)
                {
                    line.Variable = assignment.Groups[1].Value;
                    expression = assignment.Groups[2].Value.Trim();
                }
                else if (LooksLikeMemo(expression, sheet.Variables)) { line.IsMemo = true; continue; }
                if (line.Variable != null) line.AssignedExpression = expression;
                try
                {
                    if (expression.Length > MaxLineLength) throw new CalculationException("Line is too long");
                    if (line.Variable != null && Reserved.Contains(line.Variable)) throw new CalculationException("Reserved name: " + line.Variable);
                    if (previous.HasValue) sheet.Variables["ans"] = previous.Value;
                    string expected = null;
                    int equals = expression.IndexOf('=');
                    if (equals >= 0)
                    {
                        expected = expression.Substring(equals + 1).Trim();
                        expression = expression.Substring(0, equals).Trim();
                    }
                    // Bindings are available later in this expression, but only
                    // become sheet variables when the whole line succeeds.
                    Dictionary<string, double> pending = new Dictionary<string, double>(sheet.Variables, StringComparer.OrdinalIgnoreCase);
                    double value = new Parser(expression, pending, degrees).Run();
                    if (!String.IsNullOrEmpty(expected))
                    {
                        double expectedValue = Evaluate(expected, pending, degrees);
                        line.Matches = Math.Abs(value - expectedValue) <= 1e-12 * Math.Max(1, Math.Max(Math.Abs(value), Math.Abs(expectedValue)));
                    }
                    line.Value = value;
                    if (line.Variable != null) pending[line.Variable] = value;
                    foreach (KeyValuePair<string, double> pair in pending) sheet.Variables[pair.Key] = pair.Value;
                    if (line.Variable == null && quoted.Success)
                    {
                        // Display metadata only; the parser has already bound
                        // the quoted name to the preceding value, not the total.
                        line.Variable = quoted.Groups[2].Value;
                        line.AssignedExpression = quoted.Groups[1].Value.Trim();
                    }
                    previous = value;
                }
                catch (CalculationException ex)
                {
                    line.Error = ex.Message;
                    // A broken reassignment must not silently reuse an old value.
                    if (line.Variable != null && !Reserved.Contains(line.Variable)) sheet.Variables.Remove(line.Variable);
                    foreach (Match binding in QuotedName.Matches(expression))
                        if (!Reserved.Contains(binding.Groups[1].Value)) sheet.Variables.Remove(binding.Groups[1].Value);
                }
            }
            sheet.Variables.Remove("ans");
            return sheet;
        }

        private static bool LooksLikeMemo(string text, IDictionary<string, double> variables)
        {
            if (text.IndexOf('"') >= 0) return false;
            if (Regex.IsMatch(text, @"^[a-zA-Z_][a-zA-Z_0-9]*$"))
                return !variables.ContainsKey(text) && !Reserved.Contains(text) && !Parser.FunctionNames.Contains(text.ToLowerInvariant());
            // Plain prose is a memo. Anything containing math syntax is parsed and errors stay visible.
            return Regex.IsMatch(text, @"^[\p{L}\s'"":;.,]+$") && !Regex.IsMatch(text, @"\b(and|or|xor|not|mod)\b", RegexOptions.IgnoreCase);
        }

        public static double Evaluate(string expression, IDictionary<string, double> variables, bool degrees)
        {
            if (expression.Length > MaxLineLength) throw new CalculationException("Line is too long");
            return new Parser(expression, new Dictionary<string, double>(variables, StringComparer.OrdinalIgnoreCase), degrees).Run();
        }

        private sealed class Parser
        {
            internal static readonly HashSet<string> FunctionNames = new HashSet<string>(new[] {
                "abs", "sqrt", "root", "cbrt", "pow", "exp", "ln", "log", "log10", "log2", "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "sinh", "cosh", "tanh", "floor", "ceil", "ceiling", "round", "trunc", "truncate", "sign", "min", "max", "sum", "avg", "factorial", "fact", "mod", "deg", "rad"
            });
            private readonly string input;
            private readonly IDictionary<string, double> variables;
            private readonly bool degrees;
            private int position;
            private int depth;
            private string token;
            private string kind;
            private double number;

            internal Parser(string input, IDictionary<string, double> variables, bool degrees)
            {
                this.input = input.Replace('×', '*').Replace('÷', '/').Replace('−', '-').Replace("π", "pi").Replace("φ", "phi").Replace("√", "sqrt");
                this.input = this.input.Replace('\u201c', '"').Replace('\u201d', '"');
                this.variables = variables;
                this.degrees = degrees;
                Next();
            }
            internal double Run()
            {
                if (kind == "end") throw new CalculationException("Enter an expression");
                double value = Expression(0);
                if (kind != "end") throw new CalculationException("Unexpected '" + token + "'");
                return Finite(value);
            }
            private void Next()
            {
                while (position < input.Length && Char.IsWhiteSpace(input[position])) position++;
                if (position >= input.Length) { token = ""; kind = "end"; return; }
                int start = position;
                char c = input[position++];
                if (c == '"')
                {
                    int end = input.IndexOf('"', position);
                    if (end < 0) throw new CalculationException("Missing closing quote for variable name");
                    token = input.Substring(position, end - position); position = end + 1;
                    kind = "binding"; return;
                }
                if (Char.IsDigit(c) || c == '.')
                {
                    if (c == '0' && position < input.Length && (input[position] == 'x' || input[position] == 'X' || input[position] == 'b' || input[position] == 'B'))
                    {
                        int radix = Char.ToLowerInvariant(input[position++]) == 'x' ? 16 : 2;
                        int digits = position;
                        while (position < input.Length && Uri.IsHexDigit(input[position])) position++;
                        try { number = Convert.ToInt64(input.Substring(digits, position - digits), radix); }
                        catch (Exception) { throw new CalculationException("Invalid integer literal"); }
                        if (Math.Abs(number) > 9007199254740991d) throw new CalculationException("Integer exceeds exact range");
                    }
                    else
                    {
                        while (position < input.Length && (Char.IsDigit(input[position]) || input[position] == '.')) position++;
                        if (position < input.Length && (input[position] == 'e' || input[position] == 'E'))
                        {
                            int exponent = position + 1;
                            if (exponent < input.Length && (input[exponent] == '+' || input[exponent] == '-')) exponent++;
                            if (exponent < input.Length && Char.IsDigit(input[exponent]))
                            {
                                position = exponent + 1;
                                while (position < input.Length && Char.IsDigit(input[position])) position++;
                            }
                        }
                        if (!Double.TryParse(input.Substring(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) throw new CalculationException("Invalid number");
                    }
                    token = input.Substring(start, position - start); kind = "number"; return;
                }
                if (Char.IsLetter(c) || c == '_')
                {
                    while (position < input.Length && (Char.IsLetterOrDigit(input[position]) || input[position] == '_')) position++;
                    token = input.Substring(start, position - start).ToLowerInvariant(); kind = "name"; return;
                }
                token = c.ToString(); kind = "operator";
                if (position < input.Length && ((c == '<' && input[position] == '<') || (c == '>' && input[position] == '>') || (c == '*' && input[position] == '*'))) token += input[position++];
            }
            private double Expression(int minimum)
            {
                if (++depth > 96) throw new CalculationException("Expression is too deeply nested");
                double left;
                string current = token;
                if (kind == "binding") throw new CalculationException("Put a quoted variable name after a value");
                if (current == "+" || current == "-" || current == "~" || current == "not")
                {
                    Next(); left = Expression(65);
                    if (current == "-") left = -left;
                    if (current == "~" || current == "not") left = ~Integer(left);
                }
                else if (kind == "number") { left = number; Next(); }
                else if (current == "(") { Next(); left = Expression(0); Require(")"); }
                else if (kind == "name")
                {
                    Next();
                    if (token == "(")
                    {
                        Next(); List<double> args = new List<double>();
                        if (token != ")")
                        {
                            do { if (args.Count > 64) throw new CalculationException("Too many arguments"); args.Add(Expression(0)); if (token != ",") break; Next(); } while (true);
                        }
                        Require(")"); left = Function(current, args.ToArray());
                    }
                    else if (current == "pi") left = Math.PI;
                    else if (current == "tau") left = Math.PI * 2;
                    else if (current == "e") left = Math.E;
                    else if (current == "phi") left = (1 + Math.Sqrt(5)) / 2;
                    else if (!variables.TryGetValue(current, out left)) throw new CalculationException("Unknown variable: " + current);
                }
                else throw new CalculationException(kind == "end" ? "Incomplete expression" : "Unexpected '" + token + "'");
                while (true)
                {
                    // Below unary signs/powers, above multiplication/addition.
                    // Parenthesize a calculation to name its whole result.
                    if (kind == "binding" && minimum <= 64)
                    {
                        if (!ValidName.IsMatch(token)) throw new CalculationException("Invalid variable name: " + token);
                        if (Reserved.Contains(token)) throw new CalculationException("Reserved name: " + token);
                        variables[token] = Finite(left);
                        Next(); continue;
                    }
                    if (kind == "binding") break;
                    if (token == "!" && minimum <= 80) { Next(); left = Factorial(left); continue; }
                    string op = token;
                    int precedence = Precedence(op);
                    bool implicitMultiply = op == "(" || (kind == "name" && precedence < 0 && op != "not");
                    if (implicitMultiply) { precedence = 60; op = "*"; }
                    if (precedence < minimum) break;
                    if (!implicitMultiply) Next();
                    double right = Expression(precedence + ((op == "^" || op == "**") ? 0 : 1));
                    left = Binary(op, left, right);
                }
                depth--;
                return Finite(left);
            }
            private void Require(string expected)
            {
                if (token != expected) throw new CalculationException("Expected '" + expected + "'");
                Next();
            }
            private static int Precedence(string op)
            {
                switch (op)
                {
                    case "|": case "or": return 10;
                    case "xor": return 20;
                    case "&": case "and": return 30;
                    case "<<": case ">>": return 40;
                    case "+": case "-": return 50;
                    case "*": case "/": case "%": case "mod": return 60;
                    case "^": case "**": return 70;
                    default: return -1;
                }
            }
            private static long Integer(double value)
            {
                if (Math.Truncate(value) != value || Math.Abs(value) > 9007199254740991d) throw new CalculationException("Use exact integers (up to 2^53 - 1)");
                return (long)value;
            }
            private static double Binary(string op, double a, double b)
            {
                switch (op)
                {
                    case "+": return a + b;
                    case "-": return a - b;
                    case "*": return a * b;
                    case "/": if (b == 0) throw new CalculationException("Cannot divide by zero"); return a / b;
                    case "%": case "mod": if (b == 0) throw new CalculationException("Cannot divide by zero"); return a % b;
                    case "^": case "**": return Math.Pow(a, b);
                    case "&": case "and": return Integer(a) & Integer(b);
                    case "|": case "or": return Integer(a) | Integer(b);
                    case "xor": return Integer(a) ^ Integer(b);
                    case "<<": case ">>":
                        long count = Integer(b);
                        if (count < 0 || count > 52) throw new CalculationException("Shift must be 0 to 52");
                        long integer = Integer(a);
                        if (op == ">>") return integer >> (int)count;
                        double shifted = integer * Math.Pow(2, count);
                        Integer(shifted); return shifted;
                    default: throw new CalculationException("Unknown operator");
                }
            }
            private double Function(string name, double[] a)
            {
                int required = (name == "pow" || name == "root" || name == "atan2" || name == "mod") ? 2 : 1;
                bool aggregate = name == "min" || name == "max" || name == "sum" || name == "avg";
                bool optional = name == "round" || name == "log";
                if (!FunctionNames.Contains(name)) throw new CalculationException("Unknown function: " + name);
                if (aggregate ? a.Length < 1 : optional ? a.Length < 1 || a.Length > 2 : a.Length != required)
                    throw new CalculationException(name + ": check argument count");
                double x = a[0];
                double angle = degrees ? x * Math.PI / 180 : x;
                double inverse = degrees ? 180 / Math.PI : 1;
                switch (name)
                {
                    case "abs": return Math.Abs(x);
                    case "sqrt": return Math.Sqrt(x);
                    case "cbrt": return Math.Sign(x) * Math.Pow(Math.Abs(x), 1d / 3);
                    case "root":
                        if (a[1] == 0) throw new CalculationException("Root degree cannot be zero");
                        return x < 0 && a[1] % 2 != 0 && Math.Truncate(a[1]) == a[1] ? -Math.Pow(-x, 1 / a[1]) : Math.Pow(x, 1 / a[1]);
                    case "pow": return Math.Pow(x, a[1]);
                    case "exp": return Math.Exp(x);
                    case "ln": return Math.Log(x);
                    case "log": return a.Length == 2 ? Math.Log(x, a[1]) : Math.Log10(x);
                    case "log10": return Math.Log10(x);
                    case "log2": return Math.Log(x, 2);
                    case "sin": return Math.Sin(angle);
                    case "cos": return Math.Cos(angle);
                    case "tan": if (Math.Abs(Math.Cos(angle)) < 1e-14) throw new CalculationException("Tangent is undefined"); return Math.Tan(angle);
                    case "asin": return Math.Asin(x) * inverse;
                    case "acos": return Math.Acos(x) * inverse;
                    case "atan": return Math.Atan(x) * inverse;
                    case "atan2": return Math.Atan2(x, a[1]) * inverse;
                    case "sinh": return Math.Sinh(x);
                    case "cosh": return Math.Cosh(x);
                    case "tanh": return Math.Tanh(x);
                    case "floor": return Math.Floor(x);
                    case "ceil": case "ceiling": return Math.Ceiling(x);
                    case "trunc": case "truncate": return Math.Truncate(x);
                    case "round":
                        double digits = a.Length == 2 ? a[1] : 0;
                        if (digits < 0 || digits > 12 || Math.Truncate(digits) != digits) throw new CalculationException("Round digits: 0 to 12");
                        return Math.Round(x, (int)digits, MidpointRounding.AwayFromZero);
                    case "sign": return Math.Sign(x);
                    case "min": return a.Min();
                    case "max": return a.Max();
                    case "sum": return a.Sum();
                    case "avg": return a.Average();
                    case "factorial": case "fact": return Factorial(x);
                    case "mod": return Binary("%", x, a[1]);
                    case "deg": return x * 180 / Math.PI;
                    case "rad": return x * Math.PI / 180;
                    default: throw new CalculationException("Unknown function: " + name);
                }
            }
            private static double Factorial(double x)
            {
                if (x < 0 || x > 170 || Math.Truncate(x) != x) throw new CalculationException("Factorial needs an integer 0 to 170");
                double value = 1; for (int i = 2; i <= x; i++) value *= i; return value;
            }
            private static double Finite(double value)
            {
                if (Double.IsNaN(value)) throw new CalculationException("Outside function domain");
                if (Double.IsInfinity(value)) throw new CalculationException("Result is out of range");
                return value;
            }
        }
    }
}
