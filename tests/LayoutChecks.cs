using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            // Exercise actual WPF layout without stealing focus or using profile data.
            view.Window.ShowActivated = false; view.Window.ShowInTaskbar = false; view.Window.Opacity = 0;
            view.Window.Show(); Pump();
            TextBox editor = (TextBox)view.Window.FindName("Editor");
            Canvas answers = (Canvas)view.Window.FindName("Results");
            TextBlock answer = answers.Children.OfType<TextBlock>().First(t => t.Text == " 5");
            int end = editor.GetCharacterIndexFromLineIndex(2) + 4;
            Rect rect = editor.GetRectFromCharacterIndex(end);
            Check(Math.Abs(Canvas.GetLeft(answer) - rect.Left - 2) < 1, "Inline answer must follow the equals sign");
            Check(Math.Abs(Canvas.GetTop(answer) - rect.Top) < 2, "Inline answer baseline must align");
            Check(answers.Children.Count == 2, "Assignments must not display duplicate results");
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
            view.Window.Close();
            Console.WriteLine("PASS: " + checks + " WPF layout, scrollbar and clear-command checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
