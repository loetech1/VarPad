using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

[assembly: AssemblyTitle("VarPad")]
[assembly: AssemblyDescription("A little space to think in numbers.")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace VarPad
{
    public sealed class CalcApp : Application
    {
        internal NoteStore Store;
        internal List<Note> Notes;
        internal readonly Dictionary<string, NoteWindow> OpenNotes = new Dictionary<string, NoteWindow>();
        private Mutex mutex;
        private EventWaitHandle newNoteEvent;
        private DispatcherTimer signalTimer;
        private bool shuttingDown;

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VarPad", "Notes");
                bool customData = false;
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--data-dir") { data = Path.GetFullPath(args[i + 1]); customData = true; }
                CalcApp app = new CalcApp();
                string key;
                using (SHA256 hash = SHA256.Create()) key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(data.ToLowerInvariant()))).Replace("-", "").Substring(0, 24);
                bool first;
                app.mutex = new Mutex(true, "Local\\VarPad-" + key, out first);
                app.newNoteEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\VarPad-New-" + key);
                if (!first) { app.newNoteEvent.Set(); app.newNoteEvent.Dispose(); app.mutex.Dispose(); return; }
                app.Store = new NoteStore(data);
                if (!customData) app.Store.ImportLegacy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickierCalc", "Notes"));
                app.Notes = app.Store.Load();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.Startup += delegate { app.Start(); };
                app.Run();
                app.newNoteEvent.Dispose(); app.mutex.ReleaseMutex(); app.mutex.Dispose();
            }
            catch (Exception ex) { MessageBox.Show("VarPad could not start.\n\n" + ex.Message, "VarPad", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void Start()
        {
            if (Notes.Count == 0)
                NewNote("Scratchpad", "# Name a number. Keep your train of thought.\n25\"horses\"\nfeed_per_horse = 3\nhorses * feed_per_horse =\n\n# Both styles work together.\na = 3\n2\"v\"\na + v =\n\n# Type cls or clear, then Enter, to start fresh.\n");
            else
            {
                List<Note> restore = Notes.Where(n => n.WasOpen).Take(20).ToList();
                if (restore.Count == 0) restore.Add(Notes[0]);
                foreach (Note note in restore) ShowNote(note);
            }
            signalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            signalTimer.Tick += delegate { if (newNoteEvent.WaitOne(0)) NewNote(null, ""); };
            signalTimer.Start();
            if (Store.Warnings.Count > 0) MessageBox.Show(String.Join("\n\n", Store.Warnings.ToArray()), "Note recovery", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        internal void NewNote(string title, string text)
        {
            Note note = new Note { Title = title ?? "Note " + (Notes.Count + 1), Text = text };
            Notes.Insert(0, note); ShowNote(note);
        }
        internal void ShowNote(Note note)
        {
            NoteWindow existing;
            if (OpenNotes.TryGetValue(note.Id, out existing)) { existing.Window.WindowState = WindowState.Normal; existing.Window.Activate(); return; }
            NoteWindow window = new NoteWindow(this, note);
            OpenNotes.Add(note.Id, window);
            note.WasOpen = true;
            window.Window.Show();
        }
        internal void Closed(Note note)
        {
            OpenNotes.Remove(note.Id);
            if (!shuttingDown && OpenNotes.Count == 0) Shutdown();
        }
        internal void Quit()
        {
            shuttingDown = true;
            foreach (NoteWindow window in OpenNotes.Values.ToList())
            {
                window.PreserveOpen = true;
                window.Window.Close();
                if (OpenNotes.ContainsKey(window.Note.Id)) { shuttingDown = false; return; }
            }
            Shutdown();
        }
    }

    internal sealed class NoteWindow
    {
        internal readonly Window Window;
        internal readonly Note Note;
        internal bool PreserveOpen;
        private readonly CalcApp app;
        private readonly TextBox editor, title;
        private readonly Canvas results, numbers;
        private readonly TextBlock status;
        private readonly DispatcherTimer saveTimer, calculationTimer;
        private SheetResult sheet = new SheetResult();
        private string evaluatedText = "";
        private int renderRevision;
        private string lastRenderKey;
        private bool ready, drawing;
        private Brush ink, muted, warning;

        internal NoteWindow(CalcApp app, Note note)
        {
            this.app = app; Note = note;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
            editor = Find<TextBox>("Editor"); title = Find<TextBox>("NoteTitle");
            results = Find<Canvas>("Results"); numbers = Find<Canvas>("Numbers"); status = Find<TextBlock>("Status");
            Window.Width = note.Width; Window.Height = note.Height;
            // Keep restored windows reachable after monitor changes.
            double left = note.Left < -10000 || note.Left == -1 ? SystemParameters.WorkArea.Left + 80 + app.OpenNotes.Count * 28 : note.Left;
            double top = note.Top < -10000 || note.Top == -1 ? SystemParameters.WorkArea.Top + 80 + app.OpenNotes.Count * 28 : note.Top;
            Window.Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(left, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Window.Width));
            Window.Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(top, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80));
            Window.Title = note.Title + " · VarPad";
            using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                Window.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(icon, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            title.Text = note.Title; editor.Text = note.Text; editor.FontSize = note.FontSize;
            saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            saveTimer.Tick += delegate { saveTimer.Stop(); Save(); };
            calculationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
            calculationTimer.Tick += delegate { calculationTimer.Stop(); Recalculate(); };
            editor.TextChanged += delegate { if (ready) { results.Children.Clear(); calculationTimer.Stop(); calculationTimer.Start(); Dirty(); } };
            title.TextChanged += delegate { if (ready) { Note.Title = String.IsNullOrWhiteSpace(title.Text) ? "Untitled" : title.Text.Trim(); Window.Title = Note.Title + " · VarPad"; Dirty(); } };
            editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { DrawResults(); }));
            editor.SelectionChanged += delegate { if (ready) DrawResults(); };
            editor.LayoutUpdated += delegate { DrawResults(); };
            Window.SizeChanged += delegate { DrawResults(); if (ready) Dirty(); };
            Window.LocationChanged += delegate { if (ready) Dirty(); };
            Window.PreviewKeyDown += KeyDown;
            Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                Note.WasOpen = PreserveOpen;
                saveTimer.Stop(); calculationTimer.Stop();
                if (!Save())
                {
                    Note.WasOpen = true;
                    MessageBoxResult choice = MessageBox.Show(Window, "This note could not be saved. Keep the window open and use Ctrl+S to export a copy.\n\nClose anyway?", "Unsaved note", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (choice != MessageBoxResult.Yes) { e.Cancel = true; return; }
                }
            };
            Window.Closed += delegate { app.Closed(note); };
            Find<Border>("TitleBar").MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                // A button's template also contains Borders/TextBlocks. Never
                // start a window drag from one of the compact toolbar buttons.
                DependencyObject target = e.OriginalSource as DependencyObject;
                while (target != null)
                {
                    if (target is ButtonBase) return;
                    target = VisualTreeHelper.GetParent(target);
                }
                try { Window.DragMove(); } catch (InvalidOperationException) { }
            };
            Click("Close", delegate { Window.Close(); });
            Click("Minimize", delegate { Window.WindowState = WindowState.Minimized; });
            Click("New", delegate { app.NewNote(null, ""); });
            Click("Pin", TogglePin);
            Click("Angle", delegate { Note.Degrees = !Note.Degrees; ApplyAppearance(); Recalculate(); Dirty(); });
            Click("Notes", ShowNotes);
            Click("Settings", ShowSettings);
            Click("Help", ShowHelp);
            Click("Variables", ShowVariables);
            results.MouseWheel += delegate(object sender, MouseWheelEventArgs e) { editor.ScrollToVerticalOffset(editor.VerticalOffset - e.Delta); e.Handled = true; };
            Window.Loaded += delegate { ready = true; ApplyAppearance(); Recalculate(); editor.Focus(); editor.CaretIndex = editor.Text.Length; Save(); };
        }
        private T Find<T>(string name) where T : class { return Window.FindName(name) as T; }
        private void Click(string name, Action action) { Find<Button>(name).Click += delegate { action(); }; }
        private static SolidColorBrush Brush(string color) { return (SolidColorBrush)new BrushConverter().ConvertFromString(color); }

        private void ApplyAppearance()
        {
            renderRevision++;
            string foreground = "#9DE8AA", background = "#252927", secondary = "#93A297", line = "#3B483F";
            if (Note.Theme == "Amber") foreground = "#F1CF8C";
            if (Note.Theme == "Blue") foreground = "#9FD5F4";
            if (Note.Theme == "Dark") foreground = "#DEEBE1";
            if (Note.Theme == "Light") { foreground = "#235332"; background = "#F2F3ED"; secondary = "#637466"; line = "#CCD5CD"; }
            ink = Brush(foreground); muted = Brush(secondary); warning = Brush(Note.Theme == "Light" ? "#A03A27" : "#F1B293");
            Window.Resources["Ink"] = ink; Window.Resources["Muted"] = muted; Window.Resources["Line"] = Brush(line);
            SolidColorBrush fill = Brush(background); fill.Opacity = Note.BackgroundOpacity;
            Find<Border>("Shell").Background = fill;
            Window.Topmost = Note.Pinned;
            Find<Button>("Pin").Content = Note.Pinned ? "Pinned" : "Pin";
            Find<Button>("Pin").Background = Note.Pinned ? Brush("#304F6540") : Brushes.Transparent;
            Find<Button>("Angle").Content = Note.Degrees ? "DEG" : "RAD";
            editor.FontSize = Note.FontSize;
            DrawResults();
        }
        private void TogglePin() { Note.Pinned = !Note.Pinned; ApplyAppearance(); Dirty(); }
        private void Dirty()
        {
            Note.Text = editor.Text;
            status.Text = "Saving…";
            saveTimer.Stop(); saveTimer.Start();
        }
        private bool Save()
        {
            Note.Text = editor.Text;
            if (Window.WindowState == WindowState.Normal)
            { Note.Width = Window.Width; Note.Height = Window.Height; Note.Left = Window.Left; Note.Top = Window.Top; }
            try { app.Store.Save(Note); status.Text = "Saved on this device"; status.Foreground = muted; status.ToolTip = null; return true; }
            catch (Exception ex) { status.Text = "Could not save · Ctrl+S to export"; status.ToolTip = ex.Message; status.Foreground = warning; return false; }
        }
        private void Recalculate()
        {
            renderRevision++;
            evaluatedText = editor.Text;
            sheet = Calculator.EvaluateSheet(evaluatedText, Note.Degrees);
            Find<Button>("Variables").Content = sheet.Variables.Count + (sheet.Variables.Count == 1 ? " variable" : " variables");
            // Reserve room inside the horizontal scroll extent for the longest
            // generated suffix, without dedicating a result column on screen.
            double reserve = 12;
            foreach (LineResult line in sheet.Lines)
            {
                TextBlock measure = new TextBlock { Text = InlineAnswer.Suffix(line), FontFamily = editor.FontFamily, FontSize = line.Error == null ? editor.FontSize : 11 };
                measure.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                reserve = Math.Max(reserve, measure.DesiredSize.Width + 12);
            }
            editor.Padding = new Thickness(0, 4, reserve, 20);
            DrawResults();
        }
        private void DrawResults()
        {
            if (!ready || drawing || !editor.IsLoaded || editor.Text != evaluatedText) return;
            drawing = true;
            try
            {
                int first = Math.Max(0, editor.GetFirstVisibleLineIndex());
                int last = Math.Min(sheet.Lines.Count - 1, Math.Max(first, editor.GetLastVisibleLineIndex()));
                int current = editor.GetLineIndexFromCharacterIndex(editor.CaretIndex);
                string renderKey = String.Join("|", new object[] { renderRevision, first, last, current, editor.HorizontalOffset, editor.VerticalOffset, editor.ActualWidth, editor.ActualHeight, editor.ExtentWidth, editor.ExtentHeight, editor.ViewportWidth, editor.ViewportHeight });
                if (lastRenderKey == renderKey) return;
                lastRenderKey = renderKey;
                results.Children.Clear(); numbers.Children.Clear();
                for (int i = first; i <= last; i++)
                {
                    int charIndex = editor.GetCharacterIndexFromLineIndex(i);
                    if (charIndex < 0) continue;
                    Rect rect = editor.GetRectFromCharacterIndex(charIndex);
                    if (rect.IsEmpty) continue;
                    double y = rect.Top;
                    LineResult lineResult = sheet.Lines[i];
                    TextBlock num = new TextBlock { Text = (i + 1).ToString("00"), FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = i == current ? ink : muted, Opacity = i == current ? 1 : .55 };
                    Canvas.SetTop(num, y + (editor.FontSize - 11) / 2); Canvas.SetLeft(num, 14); numbers.Children.Add(num);
                    if (!lineResult.Value.HasValue && lineResult.Error == null) continue;
                    string suffix = InlineAnswer.Suffix(lineResult);
                    if (suffix.Length == 0) continue;
                    int endIndex = charIndex + editor.GetLineText(i).TrimEnd('\r', '\n').Length;
                    Rect end = editor.GetRectFromCharacterIndex(endIndex);
                    if (end.IsEmpty) continue;
                    bool mismatch = lineResult.Matches == false;
                    TextBlock result = new TextBlock
                    {
                        Text = suffix,
                        Foreground = lineResult.Error != null || mismatch ? warning : ink,
                        FontFamily = new FontFamily("Consolas"), FontSize = lineResult.Error != null ? 11 : editor.FontSize,
                        Cursor = lineResult.Value.HasValue ? Cursors.Hand : Cursors.Arrow,
                        ToolTip = lineResult.Error ?? (mismatch ? "Written answer differs. Calculated value: " : "Click to copy: ") + Calculator.Format(lineResult.Value.Value)
                    };
                    if (lineResult.Value.HasValue)
                    {
                        double value = lineResult.Value.Value;
                        result.MouseLeftButtonUp += delegate { Copy(Calculator.Format(value)); };
                    }
                    Canvas.SetTop(result, y + (lineResult.Error != null ? (editor.FontSize - 11) / 2 : -1)); Canvas.SetLeft(result, end.Left + 2); results.Children.Add(result);
                }
                ScrollViewer scroll = editor.Template.FindName("PART_ContentHost", editor) as ScrollViewer;
                if (scroll != null)
                    results.Clip = new RectangleGeometry(new Rect(0, 0,
                        Math.Max(0, editor.ActualWidth - (scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 6 : 0)),
                        Math.Max(0, editor.ActualHeight - (scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible ? 6 : 0))));
            }
            catch (ArgumentOutOfRangeException) { /* Text layout can lag one dispatcher frame while typing. */ }
            finally { drawing = false; }
        }
        private void Copy(string text)
        {
            try { Clipboard.SetText(text); status.Text = "Copied to clipboard"; }
            catch (System.Runtime.InteropServices.COMException) { status.Text = "Clipboard is busy · try again"; }
        }
        private void CopyResult()
        {
            Recalculate();
            int line = editor.GetLineIndexFromCharacterIndex(editor.CaretIndex);
            if (line >= 0 && line < sheet.Lines.Count && sheet.Lines[line].Value.HasValue) Copy(Calculator.Format(sheet.Lines[line].Value.Value));
        }
        private ContextMenu Menu(string button)
        {
            ContextMenu menu = new ContextMenu { Background = Brush("#252B27"), Foreground = Brush("#D2E6D7"), BorderBrush = Brush("#526258"), PlacementTarget = Find<Button>(button), Placement = PlacementMode.Bottom };
            return menu;
        }
        private static void Item(ContextMenu menu, string name, Action action, bool check)
        {
            MenuItem item = new MenuItem { Header = name, IsChecked = check };
            item.Click += delegate { action(); }; menu.Items.Add(item);
        }
        private static void Item(ContextMenu menu, string name, Action action) { Item(menu, name, action, false); }
        private void ShowNotes()
        {
            ContextMenu menu = Menu("Notes");
            foreach (Note note in app.Notes.OrderByDescending(n => n.Modified))
            { Note captured = note; Item(menu, note.Title, delegate { app.ShowNote(captured); }, app.OpenNotes.ContainsKey(note.Id)); }
            menu.Items.Add(new Separator());
            Item(menu, "Import text…   Ctrl+O", Import);
            Item(menu, "Export this note…   Ctrl+S", Export);
            Item(menu, "Duplicate note", delegate { app.NewNote(Note.Title + " (copy)", editor.Text); });
            Item(menu, "Quit all windows   Ctrl+Q", app.Quit);
            menu.IsOpen = true;
        }
        private void ShowSettings()
        {
            ContextMenu menu = Menu("Settings");
            foreach (string theme in new[] { "Green", "Dark", "Light", "Amber", "Blue" })
            { string captured = theme; Item(menu, theme + " theme", delegate { Note.Theme = captured; ApplyAppearance(); Dirty(); }, Note.Theme == theme); }
            menu.Items.Add(new Separator());
            Item(menu, "Larger text   Ctrl++", delegate { Zoom(1); });
            Item(menu, "Smaller text   Ctrl+−", delegate { Zoom(-1); });
            menu.Items.Add(new Separator());
            foreach (double opacity in new[] { 1d, .94, .85, .75, .65 })
            { double captured = opacity; Item(menu, (opacity * 100).ToString("0") + "% background opacity", delegate { Note.BackgroundOpacity = captured; ApplyAppearance(); Dirty(); }, Math.Abs(opacity - Note.BackgroundOpacity) < .01); }
            menu.IsOpen = true;
        }
        private void ShowVariables()
        {
            Recalculate();
            ContextMenu menu = Menu("Variables");
            if (sheet.Variables.Count == 0) menu.Items.Add(new MenuItem { Header = "Try a = 3 on its own line", IsEnabled = false });
            foreach (KeyValuePair<string, double> pair in sheet.Variables)
            { string value = Calculator.Format(pair.Value); Item(menu, pair.Key + " = " + value, delegate { Copy(value); }); }
            menu.IsOpen = true;
        }
        private void Zoom(int change) { Note.FontSize = Math.Max(12, Math.Min(28, Note.FontSize + change)); ApplyAppearance(); Recalculate(); Dirty(); }
        private void KeyDown(object sender, KeyEventArgs e)
        {
            ModifierKeys mod = Keyboard.Modifiers;
            if (e.Key == Key.Return && mod == ModifierKeys.None && editor.IsKeyboardFocusWithin && TryClearCommand())
            { e.Handled = true; return; }
            if (e.Key == Key.F1) { ShowHelp(); e.Handled = true; return; }
            if ((mod & ModifierKeys.Control) == 0) return;
            if (e.Key == Key.N) app.NewNote(null, "");
            else if (e.Key == Key.S) Export();
            else if (e.Key == Key.O) Import();
            else if (e.Key == Key.Q) app.Quit();
            else if (e.Key == Key.W) Window.Close();
            else if (e.Key == Key.P && (mod & ModifierKeys.Shift) != 0) TogglePin();
            else if (e.Key == Key.C && (mod & ModifierKeys.Shift) != 0) CopyResult();
            else if (e.Key == Key.Add || e.Key == Key.OemPlus) Zoom(1);
            else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) Zoom(-1);
            else return;
            e.Handled = true;
        }
        internal bool TryClearCommand()
        {
            if (editor.SelectionLength != 0) return false;
            int line = editor.GetLineIndexFromCharacterIndex(editor.CaretIndex);
            if (line < 0) return false;
            string command = editor.GetLineText(line).Trim();
            if (!String.Equals(command, "cls", StringComparison.OrdinalIgnoreCase) &&
                !String.Equals(command, "clear", StringComparison.OrdinalIgnoreCase)) return false;
            // Keep the clearing operation in the editor undo history. No other
            // note or setting is touched; normal autosave persists the blank note.
            editor.BeginChange();
            try { editor.SelectAll(); editor.SelectedText = ""; editor.CaretIndex = 0; }
            finally { editor.EndChange(); }
            calculationTimer.Stop();
            Recalculate();
            editor.ScrollToHome();
            status.Text = "Cleared · Ctrl+Z to undo";
            return true;
        }
        private void Import()
        {
            OpenFileDialog dialog = new OpenFileDialog { Filter = "Text notes (*.txt)|*.txt|All files (*.*)|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(Window) != true) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 1024 * 1024) throw new IOException("Please choose a note smaller than 1 MB.");
                app.NewNote(Path.GetFileNameWithoutExtension(dialog.FileName), File.ReadAllText(dialog.FileName));
            }
            catch (Exception ex) { MessageBox.Show(Window, ex.Message, "Could not import"); }
        }
        private void Export()
        {
            string safeName = String.Join("_", Note.Title.Split(Path.GetInvalidFileNameChars()));
            SaveFileDialog dialog = new SaveFileDialog { Filter = "Text note (*.txt)|*.txt", FileName = safeName + ".txt", DefaultExt = ".txt", AddExtension = true };
            if (dialog.ShowDialog(Window) != true) return;
            try { File.WriteAllText(dialog.FileName, editor.Text, new UTF8Encoding(false)); status.Text = "Exported note"; }
            catch (Exception ex) { MessageBox.Show(Window, ex.Message, "Could not export"); }
        }
        private void ShowHelp()
        {
            Window help = new Window { Title = "VarPad · Quick reference", Owner = Window, Width = 620, Height = 650, MinWidth = 400, MinHeight = 350, Background = Brush("#252927"), Foreground = Brush("#B2E9BC"), WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = new FontFamily("Segoe UI") };
            StackPanel content = new StackPanel { Margin = new Thickness(26) };
            content.Children.Add(new TextBlock { Text = "A little space to think in numbers.", FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 9) });
            content.Children.Add(new TextBlock { Text = "Write naturally. Answers appear inline after = as you type.\nClick an answer to copy it. Your notes save automatically on this device.", TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Brush("#9FAFA3"), Margin = new Thickness(0, 0, 0, 20) });
            string reference;
            using (StreamReader reader = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("Help.txt"))) reference = reader.ReadToEnd();
            content.Children.Add(new TextBox { Text = reference, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Brush("#B2E9BC"), FontFamily = new FontFamily("Consolas"), FontSize = 13, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true });
            help.Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            help.Show();
        }
    }
}
