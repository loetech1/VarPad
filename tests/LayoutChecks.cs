using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VarPad;

internal static class LayoutChecks
{
    private static int checks;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); checks++; }
    private static void Pump()
    {
        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            CalcApp app = new CalcApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Store = new NoteStore(args[0]);
            app.Notes = new System.Collections.Generic.List<Note>();
            Note note = new Note { Text = "a=3\nv=2\na+v=\na+v" };
            NoteWindow view = new NoteWindow(app, note);
            app.Notes.Add(note); app.OpenNotes.Add(note.Id, view);
            // Exercise actual WPF layout without stealing focus or using profile data.
            view.Window.ShowActivated = false; view.Window.ShowInTaskbar = false; view.Window.Opacity = 0;
            view.Window.Show(); Pump();
            TextBox editor = (TextBox)view.Window.FindName("Editor");
            editor.Text = "25\"horses\"\nhor"; editor.CaretIndex = editor.Text.Length;
            Check(view.HandleCompletionKey(Key.Tab, ModifierKeys.None), "Tab completes before the calculation timer fires");
            Check(editor.Text == "25\"horses\"\nhorses", "Tab inserts the quoted variable name");
            editor.Undo(); Check(editor.Text == "25\"horses\"\nhor", "Completion is a single undoable edit");
            editor.Text = "sq"; editor.CaretIndex = 2;
            Check(view.HandleCompletionKey(Key.Tab, ModifierKeys.None), "Tab completes function");
            Check(editor.Text == "sqrt()" && editor.CaretIndex == 5, "Function completion places caret inside brackets");
            editor.Text = "sq(25)"; editor.CaretIndex = 2;
            view.HandleCompletionKey(Key.Tab, ModifierKeys.None);
            Check(editor.Text == "sqrt(25)", "Function completion keeps existing arguments");
            editor.Text = "horses=25\nhorseshoe=2\nhor"; editor.CaretIndex = editor.Text.Length;
            Check(view.ShowCompletion(false), "Multiple suggestions open");
            Pump();
            Check(view.HandleCompletionKey(Key.Down, ModifierKeys.None), "Down selects next suggestion");
            if (args.Length > 1)
            {
                Border popup = (Border)typeof(NoteWindow).GetField("completionBorder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
                Check(popup.ActualWidth > 200 && popup.ActualHeight > 35, "Suggestion popup lays out its rows");
                popup.Opacity = 1;
                RenderTargetBitmap preview = new RenderTargetBitmap((int)Math.Ceiling(popup.ActualWidth), (int)Math.Ceiling(popup.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                preview.Render(popup); popup.Opacity = 0;
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
                using (FileStream image = File.Create(Path.Combine(Path.GetDirectoryName(args[1]), "completion-preview.png"))) encoder.Save(image);
            }
            view.HandleCompletionKey(Key.Tab, ModifierKeys.None);
            Check(editor.Text.EndsWith("\nhorseshoe"), "Tab accepts selected suggestion");
            editor.Text = "sq"; editor.CaretIndex = 2; view.ShowCompletion(false);
            Check(view.HandleCompletionKey(Key.Escape, ModifierKeys.None) && !view.AcceptCompletion() && editor.Text == "sq", "Escape dismisses without editing");
            view.ShowCompletion(false);
            Check(!view.HandleCompletionKey(Key.Return, ModifierKeys.None) && !view.AcceptCompletion(), "Enter keeps normal newline behaviour");
            editor.Text = "# sq"; editor.CaretIndex = editor.Text.Length;
            Check(!view.HandleCompletionKey(Key.Tab, ModifierKeys.None), "Tab retains indentation when no completion exists");
            editor.Text = "sq"; editor.SelectAll();
            Check(!view.HandleCompletionKey(Key.Tab, ModifierKeys.None), "Tab does not autocomplete selected text");
            view.ToggleWrap(); editor.Text = "sq"; editor.CaretIndex = 2;
            view.HandleCompletionKey(Key.Tab, ModifierKeys.None);
            Check(editor.Text == "sqrt()", "Completion works with wrapping enabled");
            view.ToggleWrap();
            editor.Text = "a=3\nv=2\na+v=\na+v"; Pump();
            Canvas answers = (Canvas)view.Window.FindName("Results");
            Canvas commas = (Canvas)view.Window.FindName("InputCommas");
            string groupedInput = "horses=123456789\nfeed=12345.67\nmax(10000,20000)=\n# 123456 is a note\n3\"herd123456\"\nhorses+feed=";
            editor.Text = groupedInput; editor.CaretIndex = 9; Pump();
            Check(commas.Children.Count == 5, "Input displays five grouping commas without changing comments or quoted names");
            Check(editor.Text == groupedInput && editor.CaretIndex == 9, "Input grouping preserves source text and caret");
            Check(answers.Children.OfType<TextBlock>().Any(t => t.Text == " 20,000"), "Function argument commas retain their calculation meaning");
            if (args.Length > 1)
            {
                FrameworkElement shell = (FrameworkElement)view.Window.Content;
                RenderTargetBitmap preview = new RenderTargetBitmap((int)shell.ActualWidth, (int)shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                preview.Render(shell);
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
                using (FileStream image = File.Create(Path.Combine(Path.GetDirectoryName(args[1]), "input-grouping-preview.png"))) encoder.Save(image);
            }
            editor.Select(9, 0); editor.SelectedText = "0"; Pump();
            editor.Undo(); Pump(); Check(editor.Text == groupedInput, "Input grouping does not add undo operations");
            view.ToggleWrap(); Pump();
            Check(commas.Children.Count == 5 && editor.Text == groupedInput, "Input grouping works with wrapping");
            view.ToggleWrap(); editor.Text = "a=3\nv=2\na+v=\na+v"; Pump();
            TextBlock answer = answers.Children.OfType<TextBlock>().First(t => t.Text == " 5");
            int end = editor.GetCharacterIndexFromLineIndex(2) + 4;
            Rect rect = editor.GetRectFromCharacterIndex(end);
            Check(Math.Abs(Canvas.GetLeft(answer) - rect.Left - 2) < 1, "Inline answer must follow the equals sign");
            Check(Math.Abs(Canvas.GetTop(answer) - rect.Top) < 2, "Inline answer baseline must align");
            Check(answers.Children.Count == 2, "Assignments must not display duplicate results");
            editor.Text = "3\"teabage\"+2=\nteabage*4"; Pump();
            Check(answers.Children.OfType<TextBlock>().Any(t => t.Text == " 5"), "Inline variable definition must show the requested result");
            Check(answers.Children.OfType<TextBlock>().Any(t => t.Text == " = 12"), "Following line must use the inline variable");
            Check(((Button)view.Window.FindName("Variables")).Content.ToString() == "1 variable", "Inline binding must appear in variable count");
            editor.Text = "4\"teabage\"+2=\nteabage*4"; Pump();
            Check(answers.Children.OfType<TextBlock>().Any(t => t.Text == " 6") && answers.Children.OfType<TextBlock>().Any(t => t.Text == " = 16"), "Editing inline binding updates both results");
            Check(Grid.GetColumn(editor) == Grid.GetColumn(answers), "Answers must share the editor column");
            editor.Text = "a=8\nv=2\na+v="; Pump();
            Check(answers.Children.OfType<TextBlock>().Any(t => t.Text == " 10"), "Inline result must refresh after editing");
            Check(editor.Text == "a=8\nv=2\na+v=", "Generated result must not corrupt input");
            editor.Text = "25\"horses\"\nfeed=3\nhorses*feed="; Pump();
            Check(answers.Children.OfType<TextBlock>().Any(t => t.Text == " 75"), "Quoted variable must calculate in the live editor");
            Check(answers.Children.Count == 1, "Quoted literals must not display redundant answers");
            Check(view.Window.Title.EndsWith("VarPad"), "Window must carry the new app name");
            editor.Text = String.Join("\n", Enumerable.Repeat("1234567890+1234567890+1234567890+1234567890=", 80));
            Pump();
            ScrollViewer scroll = (ScrollViewer)editor.Template.FindName("PART_ContentHost", editor);
            ScrollBar vertical = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll);
            ScrollBar horizontal = (ScrollBar)scroll.Template.FindName("PART_HorizontalScrollBar", scroll);
            Check(vertical.IsVisible && Math.Abs(vertical.ActualWidth - 6) < .1, "Vertical scrollbar must be six pixels");
            Check(horizontal.IsVisible && Math.Abs(horizontal.ActualHeight - 6) < .1, "Horizontal scrollbar must be six pixels");
            Check(scroll.ScrollableWidth > 0, "Horizontal extent must include generated answers");
            editor.ScrollToEnd(); Pump(); editor.ScrollToHorizontalOffset(scroll.ScrollableWidth); Pump();
            Check(editor.VerticalOffset > 0 && editor.HorizontalOffset > 0, "Both scroll directions must work");
            Check(answers.Children.OfType<TextBlock>().Any(), "Answers must remain visible after scrolling");
            Check(answers.Children.OfType<TextBlock>().All(t => Canvas.GetLeft(t) + t.ActualWidth <= editor.ActualWidth), "Scrolled answers must fit inside the viewport");
            view.Window.Width = 400; Pump();
            FrameworkElement writingArea = (FrameworkElement)view.Window.FindName("WritingArea");
            Border titleBar = (Border)view.Window.FindName("TitleBar");
            Check(titleBar.ActualHeight == 34, "Toolbar must occupy just one compact row");
            Check(writingArea.TranslatePoint(new Point(), view.Window).Y <= 61, "Editor must start within 61 pixels of window top");
            double lastRight = 0;
            foreach (string name in new[] { "New", "Notes", "Angle", "Settings", "Help", "Pin", "Minimize", "Close" })
            {
                Button button = (Button)view.Window.FindName(name);
                Point position = button.TranslatePoint(new Point(), view.Window);
                Check(position.X >= lastRight && position.X + button.ActualWidth <= view.Window.ActualWidth, name + " must fit without overlaps at minimum width");
                lastRight = position.X + button.ActualWidth;
            }
            Note other = new Note { Text = "keep=42", Title = "Another note" };
            app.Store.Save(other);
            editor.Text = "a=3\na+2\nclear"; editor.CaretIndex = editor.Text.Length; Pump();
            Check(editor.Text.EndsWith("clear"), "Typing clear must not execute without Enter");
            Check(view.TryClearCommand(), "Clear command must be accepted"); Pump();
            Check(editor.Text == "" && editor.CaretIndex == 0 && answers.Children.Count == 0, "Clear must reset text, caret and inline answers");
            Check(((Button)view.Window.FindName("Variables")).Content.ToString() == "0 variables", "Clear must remove variable bindings");
            Check(editor.CanUndo, "Clear must remain undoable");
            editor.Undo(); Pump();
            Check(editor.Text == "a=3\na+2\nclear", "Undo must restore the entire cleared note");
            foreach (string command in new[] { "cls", " CLEAR ", "\tClS\t" })
            {
                editor.Text = "a=9\n" + command + "\na+1";
                editor.CaretIndex = editor.GetCharacterIndexFromLineIndex(1) + command.Length;
                Check(view.TryClearCommand() && editor.Text.Length == 0, "Command on current line must clear note: " + command);
            }
            foreach (string ordinary in new[] { "clear=4", "cls + 2", "# clear", "please clear", "clear()" })
            {
                editor.Text = ordinary; editor.CaretIndex = ordinary.Length;
                Check(!view.TryClearCommand() && editor.Text == ordinary, "Ordinary expressions and comments must not execute: " + ordinary);
            }
            editor.Text = "clear"; editor.SelectAll();
            Check(!view.TryClearCommand(), "Enter replacing a selection must not clear the note");
            editor.Select(editor.Text.Length, 0); view.TryClearCommand();
            Pump(); Pump(); Pump(); Pump();
            Check(app.Store.Load().Single(n => n.Id == note.Id).Text == "", "Cleared note must autosave");
            Check(app.Store.Load().Single(n => n.Id == other.Id).Text == "keep=42", "Clearing must leave other notes untouched");
            Check(note.Title == "Scratchpad" && note.FontSize == 17, "Clearing must retain note title and settings");
            view.Window.Width = 580;
            editor.Text = "a = 3\nv = 2\na + v =\n\n# Type cls or clear, then press Enter.\nprice = 48\nquantity = 3\nprice * quantity =";
            editor.CaretIndex = 0; editor.ScrollToHome(); Pump();
            if (args.Length > 1)
            {
                FrameworkElement shell = (FrameworkElement)view.Window.Content;
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)shell.ActualWidth, (int)shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(shell);
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream image = File.Create(args[1])) encoder.Save(image);
            }
            view.Window.Width = 400;
            string wrapExpression = String.Join("+", Enumerable.Repeat("1", 70)) + "=";
            string wrapText = wrapExpression + "\r\n# " + new string('w', 130) + "\r\n3\"tea\"+2=\r\n";
            editor.Text = wrapText; editor.CaretIndex = 0; editor.ScrollToHome(); Pump();
            view.ToggleWrap(); Pump(); Pump();
            Check(note.WrapText && editor.TextWrapping == TextWrapping.Wrap, "Wrap option enables wrapping for this note");
            Check(!horizontal.IsVisible && scroll.ScrollableWidth < 1 && editor.HorizontalOffset == 0, "Wrapping removes horizontal scrolling");
            Check(editor.LineCount > 4, "Long words and calculations must soft-wrap");
            Check(editor.Text == wrapText, "Wrapping must not insert newlines or change expressions");
            TextBlock wrappedAnswer = answers.Children.OfType<TextBlock>().Single(t => t.Text == " 70");
            Rect wrappedEnd = editor.GetRectFromCharacterIndex(wrapExpression.Length - 1, true);
            Check(Math.Abs(Canvas.GetTop(wrappedAnswer) - wrappedEnd.Top) < 2 && Math.Abs(Canvas.GetLeft(wrappedAnswer) - wrappedEnd.Left - 2) < 1, "Answer follows final wrapped segment");
            Canvas lineNumbers = (Canvas)view.Window.FindName("Numbers");
            Check(lineNumbers.Children.OfType<TextBlock>().Count(t => t.Text == "01") == 1 && lineNumbers.Children.OfType<TextBlock>().Any(t => t.Text == "03"), "Line numbers represent logical lines, not visual segments");
            Check(view.LogicalLineAt(wrapExpression.Length / 2) == 0 && view.LogicalLineAt(wrapText.IndexOf("3\"tea\"")) == 2, "Copy-result mapping tracks logical lines through CRLF and soft wraps");
            view.Window.Width = 580; Pump();
            wrappedAnswer = answers.Children.OfType<TextBlock>().Single(t => t.Text == " 70");
            wrappedEnd = editor.GetRectFromCharacterIndex(wrapExpression.Length - 1, true);
            Check(Math.Abs(Canvas.GetTop(wrappedAnswer) - wrappedEnd.Top) < 2, "Wrapped results follow window resize");
            Check(answers.Children.OfType<TextBlock>().All(t => Canvas.GetLeft(t) + t.ActualWidth <= editor.ActualWidth), "Wrapped answers stay within viewport");
            Pump(); Pump(); Pump(); Pump();
            Check(app.Store.Load().Single(n => n.Id == note.Id).WrapText, "Wrap toggle autosaves");
            Check(!app.Store.Load().Single(n => n.Id == other.Id).WrapText, "Wrap setting does not affect other notes");
            editor.Text = "unknown_variable_with_a_very_long_name+1"; Pump();
            view.Window.Width = 1800; Pump(); view.Window.Width = 400; Pump();
            Check(editor.Padding.Right <= editor.ActualWidth * .45 + 1 && editor.ViewportWidth > 100, "Long errors cannot squeeze the wrapped editor after resizing");
            TextBlock wrappedError = answers.Children.OfType<TextBlock>().Single();
            Check(Canvas.GetLeft(wrappedError) + wrappedError.ActualWidth <= editor.ActualWidth && wrappedError.ToolTip.ToString().Contains("unknown_variable_with_a_very_long_name"), "Wrapped errors fit the window with full detail in tooltip");
            editor.Text = String.Join("\n", Enumerable.Range(1, 35).Select(i => wrapExpression + " # " + i));
            Pump(); editor.ScrollToEnd(); Pump(); Pump();
            Check(editor.VerticalOffset > 0 && answers.Children.OfType<TextBlock>().Any(t => t.Text == " = 70"), "Wrapped results remain visible after scrolling to bottom");
            Check(lineNumbers.Children.OfType<TextBlock>().Any(t => t.Text == "35"), "Scrolled line numbers keep logical indices: " + String.Join(",", lineNumbers.Children.OfType<TextBlock>().Select(t => t.Text)));
            editor.Text = "# " + new string('w', 150) + " clear";
            editor.CaretIndex = editor.Text.Length; Pump();
            Check(!view.TryClearCommand(), "A wrapped continuation ending in clear must not clear the note");
            editor.Text += "\nclear"; editor.CaretIndex = editor.Text.Length;
            Check(view.TryClearCommand() && editor.Text == "", "Clear command after wrapped content still works before calculation timer fires");
            editor.Undo(); Pump();
            Check(editor.Text.EndsWith("\nclear"), "Undo restores wrapped content after clear");
            editor.Text = wrapText; editor.CaretIndex = 0; editor.ScrollToHome(); Pump();
            if (args.Length > 1)
            {
                FrameworkElement shell = (FrameworkElement)view.Window.Content;
                RenderTargetBitmap bitmap = new RenderTargetBitmap((int)shell.ActualWidth, (int)shell.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(shell);
                PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (FileStream image = File.Create(Path.Combine(Path.GetDirectoryName(args[1]), "wrap-preview.png"))) encoder.Save(image);
            }
            view.ToggleWrap(); Pump();
            Check(!note.WrapText && horizontal.IsVisible && editor.Text == wrapText, "Turning wrap off restores horizontal scrolling without changing content");
            app.Notes.Add(other);
            editor.Text = "unsaved=123";
            Check(!view.TryDeleteNote(false) && view.Window.IsVisible && app.Notes.Contains(note), "Cancel deletion preserves note and window");
            Check(view.TryDeleteNote(true), "Confirmed deletion succeeds with a pending autosave");
            NoteWindow reopened = app.OpenNotes[other.Id];
            reopened.Window.Opacity = 0; reopened.Window.ShowInTaskbar = false;
            Pump(); Pump(); Pump(); Pump();
            Check(!app.Notes.Contains(note) && !app.OpenNotes.ContainsKey(note.Id), "Deleted note leaves notes menu and open-window registry");
            Check(app.Store.Load().All(n => n.Id != note.Id), "Closing and queued autosave must not recreate deleted note");
            Check(reopened.Window.IsVisible && other.Text == "keep=42", "Last open note deletion reopens remaining saved note");
            Check(reopened.TryDeleteNote(true), "Final saved note can be deleted");
            NoteWindow blank = app.OpenNotes.Values.Single();
            blank.Window.Opacity = 0; blank.Window.ShowInTaskbar = false; Pump();
            Check(app.Notes.Count == 1 && blank.Note.Text == "" && blank.Note.Id != other.Id, "Deleting final note creates a fresh blank scratchpad");
            Check(app.Store.Load().All(n => n.Id != note.Id && n.Id != other.Id), "Neither deleted note returns after save");
            blank.Window.Close();
            Console.WriteLine("PASS: " + checks + " WPF layout, scrollbar, clear-command and deletion checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
