using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VarPad;

// Captures the real WPF view with synthetic demonstration notes. No personal
// notes or desktop content are read. Images are rendered at 2x for README use.
internal static class Screenshots
{
    private static void Pump()
    {
        DispatcherFrame frame = new DispatcherFrame();
        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Directory.CreateDirectory(args[1]);
            CalcApp app = new CalcApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Store = new NoteStore(args[0]); app.Notes = new List<Note>();
            List<NoteWindow> views = new List<NoteWindow>();
            Capture(app, views, args[1], "variables", "Two ways to name a number", "Green",
                "# Start with a value or start with a name.\n25\"horses\"\nfeed_per_horse = 3\nhorses * feed_per_horse =\n\n# Mix the two styles freely.\na = 3\n2\"v\"\na + v =\n\n# Expressions can have names too.\n(20 + 5)\"herd\"\nherd * 4 =");
            Capture(app, views, args[1], "estimates", "Weekend workshop estimate", "Green",
                "# Materials\n48\"timber\"\n18.5\"hardware\"\nmaterials = timber + hardware\n\n# Time and a little contingency\n3.5\"hours\"\nrate = 45\nlabour = hours * rate\nsubtotal = materials + labour\nsubtotal * 1.1 =\n\n# Edit any input. The estimate follows.");
            Capture(app, views, args[1], "science", "A pocketful of maths", "Blue",
                "# Geometry · degrees mode\nradius = 12\npi * radius^2 =\nsqrt(3^2 + 4^2) =\nsin(30) =\n\n# Numbers, powers & bits\n2^10 =\n5! =\n0xff & 0b1111 =\navg(12, 18, 24) =\n\n# Fresh page? Type cls, then Enter.");
            foreach (NoteWindow view in views) view.Window.Close();
            Console.WriteLine("Rendered 3 real VarPad views with demonstration data."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Capture(CalcApp app, List<NoteWindow> views, string folder, string file, string title, string theme, string text)
    {
        Note note = new Note { Title = title, Text = text, Width = 620, Height = 420, Theme = theme, FontSize = 17 };
        NoteWindow view = new NoteWindow(app, note); views.Add(view); app.OpenNotes.Add(note.Id, view);
        view.Window.ShowActivated = false; view.Window.ShowInTaskbar = false; view.Window.Opacity = 0;
        view.Window.Show(); Pump();
        TextBox editor = (TextBox)view.Window.FindName("Editor"); editor.CaretIndex = 0; editor.ScrollToHome();
        editor.CaretBrush = Brushes.Transparent; Keyboard.ClearFocus(); Pump();
        FrameworkElement shell = (FrameworkElement)view.Window.Content;
        // The padding gives the rounded translucent app window a quiet backdrop.
        double width = shell.ActualWidth + 48, height = shell.ActualHeight + 48;
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(new LinearGradientBrush(Color.FromRgb(25, 39, 35), Color.FromRgb(15, 21, 26), 35), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(shell), null, new Rect(24, 24, shell.ActualWidth, shell.ActualHeight));
        }
        RenderTargetBitmap bitmap = new RenderTargetBitmap((int)(width * 2), (int)(height * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (FileStream stream = File.Create(Path.Combine(folder, file + ".png"))) encoder.Save(stream);
    }
}
