using System;
using System.Globalization;

namespace VarPad
{
    // Generated answers stay separate from editable expressions so recalculation,
    // undo, and variable assignments never treat a previous answer as input.
    public static class InlineAnswer
    {
        public static string Suffix(LineResult line)
        {
            if (line.Error != null) return "  ! " + line.Error;
            if (!line.Value.HasValue) return "";
            string expression = line.Source.TrimEnd();
            string value = Calculator.Format(line.Value.Value);
            if (line.Matches == true) return ""; // The user already wrote the correct answer.
            if (line.Matches == false) return "  → " + value;
            if (expression.EndsWith("=", StringComparison.Ordinal)) return " " + value;
            if (line.Variable != null)
            {
                string rhs = line.AssignedExpression;
                double literal;
                // a = 3 is complete as written; a = 1 + 2 still gets its result.
                if (Double.TryParse(rhs, NumberStyles.Float, CultureInfo.InvariantCulture, out literal)) return "";
            }
            return " = " + value;
        }
    }
}
