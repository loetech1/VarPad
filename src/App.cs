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
[assembly: AssemblyVersion("1.4.2.0")]
[assembly: AssemblyFileVersion("1.4.2.0")]

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
        private readonly Canvas results, numbers, inputCommas;
        private readonly TextBlock status;
        private readonly DispatcherTimer saveTimer, calculationTimer;
        private SheetResult sheet = new SheetResult();
        private string evaluatedText = "";
        private int[] lineStarts = new[] { 0 };
        private int renderRevision;
        private string lastRenderKey;
        private bool ready, drawing, deleted;
        private Brush ink, muted, warning;
        private readonly Popup completionPopup;
        private readonly ListBox completionList;
        private readonly Border completionBorder;
        private CompletionMatch completionMatch;
        private bool completionPending, completing;

        internal NoteWindow(CalcApp app, Note note)
        {
            this.app = app; Note = note;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
            editor = Find<TextBox>("Editor"); title = Find<TextBox>("NoteTitle");
            completionList = new ListBox { DisplayMemberPath = "Label", Focusable = false, IsTabStop = false,
                FontFamily = new FontFamily("Consolas"), FontSize = 13, MaxHeight = 190, MinWidth = 240,
                BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            ScrollViewer.SetHorizontalScrollBarVisibility(completionList, ScrollBarVisibility.Disabled);
            completionBorder = new Border { Child = completionList, BorderThickness = new Thickness(1), Padding = new Thickness(4), CornerRadius = new CornerRadius(5) };
            completionBorder.Resources = Window.Resources;
            completionList.ItemContainerStyle = (Style)Window.Resources["CompletionItem"];
            completionList.Resources.Add(typeof(ScrollViewer), Window.Resources["SlimScrollViewer"]);
            completionPopup = new Popup { Child = completionBorder, PlacementTarget = editor, Placement = PlacementMode.Relative,
                AllowsTransparency = true, StaysOpen = true, Focusable = false };
            completionList.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                ListBoxItem item = ItemsControl.ContainerFromElement(completionList, e.OriginalSource as DependencyObject) as ListBoxItem;
                if (item == null) return;
                completionList.SelectedItem = item.Content;
                AcceptCompletion(); e.Handled = true;
            };
            results = Find<Canvas>("Results"); numbers = Find<Canvas>("Numbers"); status = Find<TextBlock>("Status");
            inputCommas = Find<Canvas>("InputCommas");
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
            calculationTimer.Tick += delegate
            {
                calculationTimer.Stop(); Recalculate();
                Window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(delegate
                {
                    if (completionPending && editor.IsKeyboardFocusWithin) ShowCompletion(false);
                    completionPending = false;
                }));
            };
            editor.TextChanged += delegate { if (ready) { CloseCompletion(); completionPending = !completing && editor.IsKeyboardFocusWithin; results.Children.Clear(); inputCommas.Children.Clear(); calculationTimer.Stop(); calculationTimer.Start(); Dirty(); } };
            title.TextChanged += delegate { if (ready) { Note.Title = String.IsNullOrWhiteSpace(title.Text) ? "Untitled" : title.Text.Trim(); Window.Title = Note.Title + " · VarPad"; Dirty(); } };
            editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { CloseCompletion(); DrawResults(); }));
            editor.SelectionChanged += delegate { CloseCompletion(); if (ready) DrawResults(); };
            editor.LostKeyboardFocus += delegate { completionPending = false; CloseCompletion(); };
            editor.PreviewMouseDown += delegate { completionPending = false; CloseCompletion(); };
            Window.Deactivated += delegate { completionPending = false; CloseCompletion(); };
            editor.LayoutUpdated += delegate { DrawResults(); };
            Window.SizeChanged += delegate { CloseCompletion(); DrawResults(); if (ready) Dirty(); };
            editor.SizeChanged += delegate { if (ready && Note.WrapText) UpdateResultPadding(); DrawResults(); };
            Window.LocationChanged += delegate { CloseCompletion(); if (ready) Dirty(); };
            Window.PreviewKeyDown += KeyDown;
            Window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                completionPending = false; CloseCompletion();
                Note.WasOpen = PreserveOpen;
                saveTimer.Stop(); calculationTimer.Stop();
                if (deleted) return;
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
            completionBorder.Background = Brush(background); completionBorder.BorderBrush = Brush(line); completionList.Foreground = ink;
            Window.Resources["Ink"] = ink; Window.Resources["Muted"] = muted; Window.Resources["Line"] = Brush(line);
            SolidColorBrush fill = Brush(background); fill.Opacity = Note.BackgroundOpacity;
            Find<Border>("Shell").Background = fill;
            Window.Topmost = Note.Pinned;
            Find<Button>("Pin").Content = Note.Pinned ? "Pinned" : "Pin";
            Find<Button>("Pin").Background = Note.Pinned ? Brush("#304F6540") : Brushes.Transparent;
            Find<Button>("Angle").Content = Note.Degrees ? "DEG" : "RAD";
            editor.FontSize = Note.FontSize;
            editor.TextWrapping = Note.WrapText ? TextWrapping.Wrap : TextWrapping.NoWrap;
            editor.HorizontalScrollBarVisibility = Note.WrapText ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            DrawResults();
        }
        private void TogglePin() { Note.Pinned = !Note.Pinned; ApplyAppearance(); Dirty(); }
        private void Dirty()
        {
            if (deleted) return;
            Note.Text = editor.Text;
            status.Text = "Saving…";
            saveTimer.Stop(); saveTimer.Start();
        }
        private bool Save()
        {
            if (deleted) return true;
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
            List<int> starts = new List<int> { 0 };
            for (int i = 0; i < evaluatedText.Length; i++)
            {
                if (evaluatedText[i] == '\r')
                {
                    if (i + 1 < evaluatedText.Length && evaluatedText[i + 1] == '\n') i++;
                    starts.Add(i + 1);
                }
                else if (evaluatedText[i] == '\n') starts.Add(i + 1);
            }
            lineStarts = starts.ToArray();
            sheet = Calculator.EvaluateSheet(evaluatedText, Note.Degrees);
            Find<Button>("Variables").Content = sheet.Variables.Count + (sheet.Variables.Count == 1 ? " variable" : " variables");
            UpdateResultPadding();
            DrawResults();
        }
        private void UpdateResultPadding()
        {
            // Reserve room inside the horizontal scroll extent for the longest
            // generated suffix, without dedicating a result column on screen.
            double reserve = 12;
            foreach (LineResult line in sheet.Lines)
            {
                TextBlock measure = new TextBlock { Text = InlineAnswer.Suffix(line), FontFamily = editor.FontFamily, FontSize = line.Error == null ? editor.FontSize : 11 };
                measure.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                reserve = Math.Max(reserve, measure.DesiredSize.Width + 12);
            }
            if (Note.WrapText) reserve = Math.Min(reserve, Math.Max(12, editor.ActualWidth * .45));
            editor.Padding = new Thickness(0, 4, reserve, 20);
        }
        internal int LogicalLineAt(int characterIndex)
        {
            int found = Array.BinarySearch(lineStarts, Math.Max(0, characterIndex));
            return found >= 0 ? found : Math.Max(0, ~found - 1);
        }
        private void DrawResults()
        {
            if (!ready || drawing || !editor.IsLoaded || editor.Text != evaluatedText) return;
            drawing = true;
            try
            {
                int firstVisual = editor.GetFirstVisibleLineIndex(), lastVisual = editor.GetLastVisibleLineIndex();
                if (firstVisual < 0 || lastVisual < 0) return;
                int first = LogicalLineAt(editor.GetCharacterIndexFromLineIndex(firstVisual));
                int last = Math.Min(sheet.Lines.Count - 1, LogicalLineAt(editor.GetCharacterIndexFromLineIndex(lastVisual)));
                int current = LogicalLineAt(editor.CaretIndex);
                string renderKey = String.Join("|", new object[] { renderRevision, first, last, current, editor.HorizontalOffset, editor.VerticalOffset, editor.ActualWidth, editor.ActualHeight, editor.ExtentWidth, editor.ExtentHeight, editor.ViewportWidth, editor.ViewportHeight });
                if (lastRenderKey == renderKey) return;
                lastRenderKey = renderKey;
                results.Children.Clear(); numbers.Children.Clear(); inputCommas.Children.Clear();
                for (int i = first; i <= last; i++)
                {
                    int charIndex = lineStarts[i];
                    Rect rect = editor.GetRectFromCharacterIndex(charIndex);
                    LineResult lineResult = sheet.Lines[i];
                    if (!lineResult.IsMemo)
                    {
                        foreach (int separator in InputGrouping.Separators(lineResult.Source))
                        {
                            Rect digit = editor.GetRectFromCharacterIndex(charIndex + separator - 1, true);
                            if (digit.IsEmpty || digit.Bottom < 0 || digit.Top > editor.ViewportHeight) continue;
                            // A comma sits in the gap below the digits' baseline.
                            // Adorn the source without changing text or caret indices.
                            TextBlock comma = new TextBlock { Text = ",", FontFamily = editor.FontFamily,
                                FontSize = editor.FontSize, Foreground = ink, IsHitTestVisible = false };
                            comma.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                            Canvas.SetLeft(comma, digit.Left - comma.DesiredSize.Width / 2);
                            Canvas.SetTop(comma, digit.Top + 1);
                            inputCommas.Children.Add(comma);
                        }
                    }
                    if (!rect.IsEmpty)
                    {
                        TextBlock num = new TextBlock { Text = (i + 1).ToString("00"), FontFamily = new FontFamily("Consolas"), FontSize = 11, Foreground = i == current ? ink : muted, Opacity = i == current ? 1 : .55 };
                        Canvas.SetTop(num, rect.Top + (editor.FontSize - 11) / 2); Canvas.SetLeft(num, 14); numbers.Children.Add(num);
                    }
                    if (!lineResult.Value.HasValue && lineResult.Error == null) continue;
                    string suffix = InlineAnswer.Suffix(lineResult);
                    if (suffix.Length == 0) continue;
                    int endIndex = charIndex + lineResult.Source.Length;
                    Rect end = endIndex > charIndex ? editor.GetRectFromCharacterIndex(endIndex - 1, true) : rect;
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
                        result.MouseLeftButtonUp += delegate { Copy(Calculator.FormatInput(value)); };
                    }
                    if (Note.WrapText)
                    {
                        result.MaxWidth = Math.Max(0, editor.ActualWidth - end.Left - 10);
                        result.TextTrimming = TextTrimming.CharacterEllipsis;
                    }
                    Canvas.SetTop(result, end.Top + (lineResult.Error != null ? (editor.FontSize - 11) / 2 : -1)); Canvas.SetLeft(result, end.Left + 2); results.Children.Add(result);
                }
                ScrollViewer scroll = editor.Template.FindName("PART_ContentHost", editor) as ScrollViewer;
                if (scroll != null)
                {
                    results.Clip = new RectangleGeometry(new Rect(0, 0,
                        Math.Max(0, editor.ActualWidth - (scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible ? 6 : 0)),
                        Math.Max(0, editor.ActualHeight - (scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible ? 6 : 0))));
                    inputCommas.Clip = results.Clip;
                }
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
            int line = LogicalLineAt(editor.CaretIndex);
            if (line >= 0 && line < sheet.Lines.Count && sheet.Lines[line].Value.HasValue) Copy(Calculator.FormatInput(sheet.Lines[line].Value.Value));
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
            Item(menu, "Delete this note...", ConfirmDelete);
            menu.Items.Add(new Separator());
            Item(menu, "Quit all windows   Ctrl+Q", app.Quit);
            menu.IsOpen = true;
        }
        private void ConfirmDelete()
        {
            bool confirmed = MessageBox.Show(Window,
                "Permanently delete \"" + Note.Title + "\" and its saved backup?\n\nThis cannot be undone.",
                "Delete note", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
            TryDeleteNote(confirmed);
        }
        internal bool TryDeleteNote(bool confirmed)
        {
            if (!confirmed || deleted) return false;
            try { app.Store.Delete(Note); }
            catch (Exception ex)
            {
                MessageBox.Show(Window, "Could not delete this note. It is still open.\n\n" + ex.Message,
                    "Delete note", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            deleted = true;
            saveTimer.Stop(); calculationTimer.Stop();
            app.Notes.Remove(Note);
            // Keep a working window when deleting the last open note.
            if (app.OpenNotes.Count <= 1)
            {
                Note next = app.Notes.OrderByDescending(n => n.Modified).FirstOrDefault();
                if (next != null) app.ShowNote(next);
                else app.NewNote("Scratchpad", "");
            }
            Window.Close();
            return true;
        }
        private void ShowSettings()
        {
            ContextMenu menu = Menu("Settings");
            Item(menu, "Wrap text", ToggleWrap, Note.WrapText);
            menu.Items.Add(new Separator());
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
            { double value = pair.Value; Item(menu, pair.Key + " = " + Calculator.Format(value), delegate { Copy(Calculator.FormatInput(value)); }); }
            menu.IsOpen = true;
        }
        private void Zoom(int change) { Note.FontSize = Math.Max(12, Math.Min(28, Note.FontSize + change)); ApplyAppearance(); Recalculate(); Dirty(); }
        internal void ToggleWrap()
        {
            Note.WrapText = !Note.WrapText;
            ApplyAppearance(); Recalculate();
            if (Note.WrapText) editor.ScrollToHorizontalOffset(0);
            Dirty();
        }
        private void KeyDown(object sender, KeyEventArgs e)
        {
            ModifierKeys mod = Keyboard.Modifiers;
            if (editor.IsKeyboardFocusWithin && HandleCompletionKey(e.Key, mod)) { e.Handled = true; return; }
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
        private void CloseCompletion()
        {
            completionPopup.IsOpen = false;
            completionMatch = null;
        }
        internal bool ShowCompletion(bool explicitRequest)
        {
            CloseCompletion();
            if (editor.SelectionLength != 0) return false;
            CompletionMatch match = Completion.Find(editor.Text, editor.CaretIndex, Note.Degrees, explicitRequest);
            if (match == null) return false;
            if (calculationTimer.IsEnabled) { calculationTimer.Stop(); Recalculate(); }
            editor.UpdateLayout();
            Rect caret = editor.GetRectFromCharacterIndex(editor.CaretIndex);
            if (caret.IsEmpty) { CloseCompletion(); return false; }
            completionMatch = match;
            completionList.ItemsSource = match.Items;
            completionList.SelectedIndex = 0;
            completionPopup.HorizontalOffset = Math.Max(0, Math.Min(caret.Left, editor.ActualWidth - 255));
            completionPopup.VerticalOffset = caret.Bottom + 3;
            completionBorder.Opacity = Window.Opacity;
            completionPopup.IsOpen = true;
            completionList.ScrollIntoView(completionList.SelectedItem);
            return true;
        }
        internal bool AcceptCompletion()
        {
            CompletionItem item = completionList.SelectedItem as CompletionItem;
            if (completionMatch == null || item == null || editor.SelectionLength != 0) return false;
            CompletionMatch match = completionMatch;
            int offset;
            string insertion = Completion.Insertion(editor.Text, match, item, out offset);
            CloseCompletion(); completionPending = false; completing = true;
            editor.BeginChange();
            try
            {
                editor.Select(match.Start, match.Length);
                editor.SelectedText = insertion;
                editor.CaretIndex = match.Start + offset;
            }
            finally { editor.EndChange(); completing = false; }
            return true;
        }
        internal bool HandleCompletionKey(Key key, ModifierKeys modifiers)
        {
            if (key == Key.Space && modifiers == ModifierKeys.Control) { completionPending = false; ShowCompletion(true); return true; }
            if (key == Key.Tab && modifiers == ModifierKeys.None)
            {
                completionPending = false;
                // Refresh synchronously when Tab beats the calculation debounce.
                if (!completionPopup.IsOpen) ShowCompletion(false);
                return AcceptCompletion();
            }
            if (completionPopup.IsOpen && modifiers == ModifierKeys.None && (key == Key.Up || key == Key.Down))
            {
                int count = completionList.Items.Count;
                completionList.SelectedIndex = (completionList.SelectedIndex + (key == Key.Down ? 1 : count - 1)) % count;
                completionList.ScrollIntoView(completionList.SelectedItem);
                return true;
            }
            if (key == Key.Escape)
            {
                bool visible = completionPopup.IsOpen || completionPending;
                completionPending = false; CloseCompletion(); return visible;
            }
            if (key == Key.Return || key == Key.Left || key == Key.Right || key == Key.Home || key == Key.End ||
                key == Key.Up || key == Key.Down || key == Key.PageUp || key == Key.PageDown || key == Key.Tab || modifiers != ModifierKeys.None)
            { completionPending = false; CloseCompletion(); }
            return false;
        }
        internal bool TryClearCommand()
        {
            if (editor.SelectionLength != 0) return false;
            string text = editor.Text;
            int caret = editor.CaretIndex;
            int start = caret == 0 ? 0 : text.LastIndexOfAny(new[] { '\r', '\n' }, caret - 1) + 1;
            int end = text.IndexOfAny(new[] { '\r', '\n' }, caret);
            string command = text.Substring(start, (end < 0 ? text.Length : end) - start).Trim();
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
