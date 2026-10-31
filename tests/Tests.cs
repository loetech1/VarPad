using System;
using System.Collections.Generic;
using System.IO;
using VarPad;

internal static class Tests
{
    private static int count;
    private static readonly Dictionary<string, double> Empty = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    private static void Check(bool condition, string message)
    { count++; if (!condition) throw new Exception("FAIL: " + message); }
    private static void Equal(double expected, double actual, string message)
    { Check(Math.Abs(expected - actual) <= 1e-11 * Math.Max(1, Math.Abs(expected)), message + ": expected " + expected + ", got " + actual); }
    private static void MathTest(string expression, double expected)
    { Equal(expected, Calculator.Evaluate(expression, Empty, true), expression); }
    private static void Error(string expression)
    {
        try { Calculator.Evaluate(expression, Empty, true); throw new Exception("FAIL: expected error for " + expression); }
        catch (CalculationException) { count++; }
    }
    private static int Main(string[] args)
    {
        try
        {
            MathTest("2 + 3 * 4", 14); MathTest("(2 + 3) * 4", 20);
            MathTest("2^3^2", 512); MathTest("-2^2", -4); MathTest("(-2)^2", 4); MathTest("2^-3", .125);
            MathTest("2**3", 8); MathTest("4! + 1", 25); MathTest("0!", 1); MathTest("3!!", 720);
            MathTest("10 - 3 - 2", 5); MathTest("12/3*2", 8); MathTest("12/3/2", 2);
            MathTest("-5 % 3", -2); MathTest("17 mod 5", 2); MathTest("mod(17, 5)", 2);
            MathTest("200 * 15 / 100", 30); MathTest(".5 + 0.25", .75); MathTest("1.25e3 + 2e-2", 1250.02);
            MathTest("2pi", Math.PI * 2); MathTest("2(3+4)", 14); MathTest("(2+3)(4+1)", 25);
            MathTest("2sin(30)", 1); MathTest("π + phi - φ", Math.PI); MathTest("tau", Math.PI * 2);
            MathTest("3 × 4 − 2 ÷ 2", 11);
            MathTest("sqrt(25)", 5); MathTest("cbrt(-8)", -2); MathTest("root(32, 5)", 2); MathTest("root(-32, 5)", -2);
            MathTest("abs(-5)", 5); MathTest("floor(-2.1)", -3); MathTest("ceil(-2.1)", -2); MathTest("trunc(-2.9)", -2);
            MathTest("round(2.345, 2)", 2.35); MathTest("round(-2.5)", -3); MathTest("sign(-9)", -1);
            MathTest("ln(e)", 1); MathTest("log(100)", 2); MathTest("log(8, 2)", 3); MathTest("log2(8)", 3); MathTest("log10(1000)", 3);
            MathTest("exp(1)", Math.E); MathTest("pow(2, 5)", 32);
            MathTest("sin(30)", .5); MathTest("cos(60)", .5); MathTest("tan(45)", 1);
            MathTest("asin(.5)", 30); MathTest("acos(.5)", 60); MathTest("atan(1)", 45); MathTest("atan2(1,1)", 45);
            Equal(1, Calculator.Evaluate("sin(pi/2)", Empty, false), "radian sine");
            Equal(Math.PI / 2, Calculator.Evaluate("asin(1)", Empty, false), "radian inverse");
            MathTest("rad(180)", Math.PI); MathTest("deg(pi)", 180);
            MathTest("sinh(0) + tanh(0) + cosh(0)", 1);
            MathTest("min(8,3,5)", 3); MathTest("max(8,3,5)", 8); MathTest("sum(1,2,3)", 6); MathTest("avg(2,4)", 3);
            MathTest("6 & 3", 2); MathTest("6 and 3", 2); MathTest("6 | 3", 7); MathTest("6 or 3", 7);
            MathTest("6 xor 3", 5); MathTest("~6", -7); MathTest("not 6", -7); MathTest("3 << 2", 12); MathTest("12 >> 2", 3);
            MathTest("-8 >> 1", -4); MathTest("0xff + 0b1010", 265); MathTest("1 | 2 & 4", 1); MathTest("(1 | 2) & 3", 3);
            MathTest("5^2", 25); MathTest("2^3!", 64);
            foreach (string bad in new[] { "1/0", "0/0", "1 % 0", "sqrt(-1)", "ln(0)", "log(2,1)", "asin(2)", "tan(90)", "2+", "(2+3", "2+3)", "min()", "sin(1,2)", "pow(2)", "wat(2)", "nope+1", "(-1)!", "171!", "1.2!", "2 << -1", "2 << 53", "2.5 & 1", "9007199254740992 & 1", "1 << 52 << 2", "1e309", "1..2", "0xzz", "0b102", "0x20000000000000", "round(1,20)", "round(1,1.5)", "root(2,0)", "System.IO.File.Delete(1)", "1;2" }) Error(bad);
            Error(new string('(', 100) + "1" + new string(')', 100)); Error(new string('1', 4097));
            SheetResult sheet = Calculator.EvaluateSheet("a=3\nv=2\na+v=5", true);
            Equal(5, sheet.Lines[2].Value.Value, "requested example"); Check(sheet.Lines[2].Matches == true, "written answer matches");
            sheet = Calculator.EvaluateSheet("a=9\nv=2\na+v=5\ntotal=a+v\nTOTAL*2", true);
            Equal(11, sheet.Lines[2].Value.Value, "live dependency change"); Check(sheet.Lines[2].Matches == false, "stale written result flagged"); Equal(22, sheet.Lines[4].Value.Value, "case-insensitive variables");
            sheet = Calculator.EvaluateSheet("Budget notes\n# heading 2026\na=3 # count\nv=2 // count\na+v=\nans*2", true);
            Check(sheet.Lines[0].IsMemo && sheet.Lines[1].IsMemo, "memos preserved"); Equal(5, sheet.Lines[4].Value.Value, "trailing equals"); Equal(10, sheet.Lines[5].Value.Value, "ans previous successful");
            Check(sheet.Variables.Count == 2, "ans excluded from variable list");
            sheet = Calculator.EvaluateSheet("a=3\na=1/0\na+2\nb=4\nb+1", true);
            Check(sheet.Lines[1].Error != null && sheet.Lines[2].Error != null, "failed reassignment invalidates value"); Equal(5, sheet.Lines[4].Value.Value, "error does not stop later lines");
            sheet = Calculator.EvaluateSheet("a=b+1\nb=2\npi=3\ne=2\nans=9\na+1", true);
            Check(sheet.Lines[0].Error != null, "forward reference reported");
            Check(sheet.Lines[2].Error != null && sheet.Lines[3].Error != null && sheet.Lines[4].Error != null, "reserved constants cannot change");
            Check(!sheet.Variables.ContainsKey("a"), "no invalid variable leaked");
            Check(!Calculator.EvaluateSheet("a+v", true).Lines[0].Value.HasValue, "variables scoped to note");
            Check(Calculator.Format(.1 + .2) == "0.3", "clean number display");
            Check(Calculator.EvaluateSheet("?", true).Lines[0].IsMemo, "help marker is memo");
            CultureInfoTest();
            InlineTests();
            QuotedVariableTests();
            StorageTests(args[0]);
            Console.WriteLine("PASS: " + count + " calculation and persistence checks."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void CultureInfoTest()
    {
        System.Globalization.CultureInfo old = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            MathTest("1.5+2.25", 3.75); Check(Calculator.Format(3.75) == "3.75", "stable formatting across cultures");
        }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
    }
    private static void InlineTests()
    {
        SheetResult sheet = Calculator.EvaluateSheet("a=3\nv=2\na+v=\na+v\na+v=5\na+v=9\ntotal=a+v\na=3 # memo\n1/0", true);
        Check(InlineAnswer.Suffix(sheet.Lines[0]) == "", "literal assignment has no duplicate answer");
        Check(InlineAnswer.Suffix(sheet.Lines[2]) == " 5", "answer immediately follows typed equals");
        Check(InlineAnswer.Suffix(sheet.Lines[3]) == " = 5", "expression gains inline equals and answer");
        Check(InlineAnswer.Suffix(sheet.Lines[4]) == "", "written answer is not repeated");
        Check(InlineAnswer.Suffix(sheet.Lines[5]) == "  → 5", "wrong written answer shows correction");
        Check(InlineAnswer.Suffix(sheet.Lines[6]) == " = 5", "calculated assignment shows result");
        Check(InlineAnswer.Suffix(sheet.Lines[7]) == "", "commented literal assignment is not duplicated");
        Check(InlineAnswer.Suffix(sheet.Lines[8]).Contains("Cannot divide by zero"), "inline errors remain visible");
        sheet = Calculator.EvaluateSheet("a=4\nv=2\na+v=", true);
        Check(InlineAnswer.Suffix(sheet.Lines[2]) == " 6", "inline answer updates with dependencies");
    }
    private static void StorageTests(string directory)
    {
        string path = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        NoteStore store = new NoteStore(path);
        Note note = new Note { Text = "a=3\nv=2\na+v", Title = "My maths π", BackgroundOpacity = .85, Pinned = true };
        store.Save(note);
        Note loaded = store.Load()[0];
        Check(loaded.Text == note.Text && loaded.Title == note.Title, "save and restore text including unicode");
        Check(loaded.Pinned && loaded.BackgroundOpacity == .85, "save and restore settings");
        note.Text = "a=4"; store.Save(note);
        Check(store.Load()[0].Text == "a=4", "atomic overwrite");
        string file = Path.Combine(path, note.Id + ".xml");
        Check(File.Exists(file + ".bak"), "backup exists");
        File.WriteAllText(file, "broken XML");
        List<Note> recovered = store.Load();
        Check(recovered.Count == 1 && recovered[0].Text.Contains("a=3"), "backup recovery");
        Check(store.Warnings.Count > 0 && File.ReadAllText(file) == "broken XML", "recovery reports and preserves broken file");
        NoteStore migrated = new NoteStore(Path.Combine(path, "migrated"));
        migrated.ImportLegacy(path);
        Check(migrated.Load()[0].Text.Contains("a=3"), "legacy import retains backup recovery");
        Check(File.ReadAllText(file) == "broken XML", "legacy import leaves original notes untouched");
        Note changed = migrated.Load()[0]; changed.Text = "25\"horses\""; migrated.Save(changed);
        migrated.ImportLegacy(path);
        Check(migrated.Load()[0].Text == "25\"horses\"", "legacy migration runs only once and preserves new edits");
        Note survivor = new Note { Text = "keep=42" }; store.Save(survivor);
        File.WriteAllText(file + ".tmp", "unfinished save");
        using (FileStream locked = new FileStream(file + ".bak", FileMode.Open, FileAccess.Read, FileShare.None))
        {
            try { store.Delete(note); throw new Exception("Locked backup deletion accepted"); } catch (IOException) { count++; }
            Check(File.Exists(file), "failed deletion keeps the primary note");
        }
        store.Delete(note);
        Check(!File.Exists(file) && !File.Exists(file + ".bak") && !File.Exists(file + ".tmp"), "delete removes note, backup and temporary file");
        Check(store.Load().Count == 1 && store.Load()[0].Id == survivor.Id, "deleted note stays absent after reload; other note remains");
        store.Delete(note);
        Check(store.Load().Count == 1, "deleting absent note is harmless");
        migrated.Delete(changed); migrated.ImportLegacy(path);
        Check(migrated.Load().Count == 0, "legacy import does not resurrect deleted notes");
        note.Id = "../../escape";
        try { store.Delete(note); throw new Exception("Delete path traversal accepted"); } catch (InvalidDataException) { count++; }
        try { store.Save(note); throw new Exception("Path traversal accepted"); } catch (InvalidDataException) { count++; }
    }
    private static void QuotedVariableTests()
    {
        SheetResult sheet = Calculator.EvaluateSheet("25\"horses\"\nfeed=3\nhorses*feed\nhorses=10\nhorses*feed\n5\"horses\"\nhorses*feed", true);
        Equal(75, sheet.Lines[2].Value.Value, "value-first variable usable in expressions");
        Equal(30, sheet.Lines[4].Value.Value, "name-first can reassign value-first variable");
        Equal(15, sheet.Lines[6].Value.Value, "value-first can reassign name-first variable");
        Check(InlineAnswer.Suffix(sheet.Lines[0]) == "", "value-first literal has no duplicate answer");
        sheet = Calculator.EvaluateSheet(" (20 + 5) \"Horses\" # herd\nhorses+1\n-2.5\"rate\"\n1e3\"base_cost\"\nbase_cost*rate", true);
        Equal(26, sheet.Lines[1].Value.Value, "quoted expression binding and case-insensitivity");
        Equal(-2500, sheet.Lines[4].Value.Value, "negative decimals and scientific notation");
        Check(InlineAnswer.Suffix(sheet.Lines[0]) == " = 25", "value-first expression result is shown inline");
        sheet = Calculator.EvaluateSheet("25“horses”\nhorses + 2\n5\"ans\"\n3\"pi\"", true);
        Equal(27, sheet.Lines[1].Value.Value, "smart quotes accepted");
        Check(sheet.Lines[2].Error != null && sheet.Lines[3].Error != null, "quoted names obey reserved-name rules");
        foreach (string invalid in new[] { "25\"two horses\"", "25\"2horses\"", "25\"\"", "25\"horses", "25\"horses\"junk", "25\"horses\"+1" })
            Check(Calculator.EvaluateSheet(invalid, true).Lines[0].Error != null, "invalid quoted assignment rejected: " + invalid);
        sheet = Calculator.EvaluateSheet("25\"horses\"\n(1/0)\"horses\"\nhorses*2", true);
        Check(sheet.Lines[1].Error != null && sheet.Lines[2].Error != null, "failed quoted reassignment removes stale variable");
        sheet = Calculator.EvaluateSheet("5\"_herd2\" // comment\n_herd2+1", true);
        Equal(6, sheet.Lines[1].Value.Value, "underscores and inline comments");
    }
}
