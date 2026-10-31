using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("VarPad Setup")]
[assembly: AssemblyVersion("1.2.1.0")]
[assembly: AssemblyFileVersion("1.2.1.0")]

namespace VarPadSetup
{
    internal static class Program
    {
        internal static readonly string InstallDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "VarPad");
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\VarPad";
        private static readonly string[] Payload = { "VarPad.exe", "VarPad.exe.config", "app.ico" };
        internal static string Executable { get { return Path.Combine(InstallDirectory, "VarPad.exe"); } }
        private static string StartShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "VarPad.lnk"); } }
        private static string DesktopShortcut { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "VarPad.lnk"); } }

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length == 1 && args[0] == "--verify-payload") { VerifyPayload(); return 0; }
                if (args.Length == 2 && args[0] == "--extract") { Extract(Path.GetFullPath(args[1]), true); return 0; }
                if (args.Length == 2 && args[0] == "--verify-extracted") { VerifyExtracted(Path.GetFullPath(args[1])); return 0; }
                if (args.Length == 2 && args[0] == "--remove-extracted")
                {
                    string directory = Path.GetFullPath(args[1]);
                    VerifyExtracted(directory);
                    if (!File.Exists(Path.Combine(directory, ".varpad-extracted"))) throw new IOException("This is not a test extraction.");
                    RemovePayload(directory);
                    File.Delete(Path.Combine(directory, ".varpad-extracted"));
                    if (Directory.GetFileSystemEntries(directory).Length == 0) Directory.Delete(directory);
                    return 0;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length > 0 && args[0] == "--uninstall")
                {
                    if (!String.Equals(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), InstallDirectory, StringComparison.OrdinalIgnoreCase)) throw new IOException("Run uninstall from the installed copy of VarPad.");
                    string temp = Path.Combine(Path.GetTempPath(), "VarPad-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
                    File.Copy(Assembly.GetExecutingAssembly().Location, temp);
                    Process.Start(new ProcessStartInfo(temp, "--uninstall-worker " + Process.GetCurrentProcess().Id) { UseShellExecute = false });
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--uninstall-worker")
                {
                    int parent;
                    if (Int32.TryParse(args[1], out parent))
                    { try { using (Process process = Process.GetProcessById(parent)) process.WaitForExit(5000); } catch (ArgumentException) { } }
                    Uninstall(); return 0;
                }
                Application.Run(new SetupWindow());
                return 0;
            }
            catch (Exception ex)
            {
                if (args.Length > 0 && (args[0] == "--verify-payload" || args[0] == "--extract" || args[0] == "--verify-extracted" || args[0] == "--remove-extracted")) return 1;
                MessageBox.Show(ex.Message, "VarPad Setup", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }

        private static byte[] ReadResource(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload." + name))
            {
                if (stream == null) throw new IOException("Installer payload is missing: " + name);
                using (MemoryStream buffer = new MemoryStream()) { stream.CopyTo(buffer); return buffer.ToArray(); }
            }
        }
        private static string Hash(byte[] bytes)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static void VerifyPayload()
        {
            string[] entries = Encoding.UTF8.GetString(ReadResource("sha256.txt")).Trim('\uFEFF', '\r', '\n').Split('\n');
            if (entries.Length != Payload.Length) throw new IOException("Invalid payload manifest.");
            for (int i = 0; i < Payload.Length; i++)
                if (Hash(ReadResource(Payload[i])) != entries[i].Trim()) throw new IOException("Installer verification failed. Please rebuild or download a fresh installer.");
        }
        private static void Extract(string directory, bool test)
        {
            VerifyPayload();
            if (test && Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length > 0) throw new IOException("Choose an empty extraction directory.");
            Directory.CreateDirectory(directory);
            foreach (string file in Payload) File.WriteAllBytes(Path.Combine(directory, file), ReadResource(file));
            File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(directory, "Uninstall.exe"), true);
            if (test) File.WriteAllText(Path.Combine(directory, ".varpad-extracted"), "VarPad installer test extraction 1.0");
        }
        private static void VerifyExtracted(string directory)
        {
            VerifyPayload();
            foreach (string file in Payload)
                if (Hash(File.ReadAllBytes(Path.Combine(directory, file))) != Hash(ReadResource(file))) throw new IOException("Extracted payload differs: " + file);
            if (Hash(File.ReadAllBytes(Path.Combine(directory, "Uninstall.exe"))) != Hash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))) throw new IOException("Uninstaller differs.");
        }
        internal static void Install(bool desktop)
        {
            EnsureClosed();
            Extract(InstallDirectory, false);
            Shortcut(StartShortcut);
            if (desktop) Shortcut(DesktopShortcut);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                key.SetValue("DisplayName", "VarPad");
                key.SetValue("DisplayVersion", "1.2.1");
                key.SetValue("Publisher", "VarPad");
                key.SetValue("InstallLocation", InstallDirectory);
                key.SetValue("DisplayIcon", Executable);
                key.SetValue("UninstallString", "\"" + Path.Combine(InstallDirectory, "Uninstall.exe") + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long bytes = 0; foreach (string file in Directory.GetFiles(InstallDirectory)) bytes += new FileInfo(file).Length;
                key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
            }
        }
        private static void Shortcut(string path)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            object shortcut = null;
            try
            {
                shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                Type sc = shortcut.GetType();
                sc.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { Executable });
                sc.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { InstallDirectory });
                sc.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "A little space to think in numbers." });
                sc.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(InstallDirectory, "app.ico") });
                sc.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        private static void EnsureClosed()
        {
            foreach (Process process in Process.GetProcessesByName("VarPad"))
            {
                using (process)
                {
                    try
                    {
                        if (String.Equals(process.MainModule.FileName, Executable, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("Please close VarPad's open windows, then try again. Your notes are kept.");
                    }
                    catch (System.ComponentModel.Win32Exception) { throw new IOException("Please close VarPad before continuing."); }
                }
            }
        }
        private static void RemovePayload(string directory)
        {
            // Remove only our known files; never recurse through an installation directory.
            foreach (string file in Payload) File.Delete(Path.Combine(directory, file));
            File.Delete(Path.Combine(directory, "Uninstall.exe"));
        }
        private static void Uninstall()
        {
            if (MessageBox.Show("Uninstall VarPad?\n\nYour saved notes will be kept on this device.", "VarPad", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            EnsureClosed();
            RemovePayload(InstallDirectory);
            if (Directory.Exists(InstallDirectory) && Directory.GetFileSystemEntries(InstallDirectory).Length == 0) Directory.Delete(InstallDirectory);
            File.Delete(StartShortcut); File.Delete(DesktopShortcut);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
            MessageBox.Show("VarPad has been uninstalled. Your saved notes have been kept.", "VarPad", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    internal sealed class SetupWindow : Form
    {
        private readonly Button install;
        private readonly CheckBox desktop;
        private readonly Label message;
        private bool installed;
        internal SetupWindow()
        {
            Text = "VarPad Setup"; ClientSize = new Size(490, 370); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; BackColor = Color.FromArgb(37, 41, 39); ForeColor = Color.FromArgb(172, 235, 184); Font = new Font("Segoe UI", 10);
            try { using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.app.ico")) Icon = new Icon(s); } catch { }
            Controls.Add(new Label { Text = "VarPad", Font = new Font("Segoe UI", 25, FontStyle.Bold), AutoSize = true, Location = new Point(30, 25) });
            Controls.Add(new Label { Text = "A little space to think in numbers.", AutoSize = true, Location = new Point(33, 80), ForeColor = Color.FromArgb(157, 179, 163) });
            message = new Label { Text = "Install for your Windows account.\nNo account, subscription or administrator access needed.\n\n" + Program.InstallDirectory, Location = new Point(33, 128), Size = new Size(425, 100), Font = new Font("Segoe UI", 9), ForeColor = Color.FromArgb(203, 215, 207) };
            Controls.Add(message);
            desktop = new CheckBox { Text = "Add a desktop shortcut", Checked = true, AutoSize = true, Location = new Point(33, 245) }; Controls.Add(desktop);
            install = new Button { Text = "Install VarPad", Location = new Point(250, 301), Size = new Size(208, 39), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(155, 226, 169), ForeColor = Color.FromArgb(28, 48, 33) };
            install.FlatAppearance.BorderSize = 0; install.Click += InstallClick; Controls.Add(install);
            Button cancel = new Button { Text = "Close", Location = new Point(33, 301), Size = new Size(90, 39), FlatStyle = FlatStyle.Flat }; cancel.FlatAppearance.BorderColor = Color.FromArgb(72, 91, 78); cancel.Click += delegate { Close(); }; Controls.Add(cancel);
            AcceptButton = install; CancelButton = cancel;
        }
        private void InstallClick(object sender, EventArgs e)
        {
            if (installed) { Process.Start(Program.Executable); Close(); return; }
            install.Enabled = false; install.Text = "Installing…"; Refresh();
            try
            {
                Program.Install(desktop.Checked); installed = true; desktop.Visible = false;
                message.Text = "You're ready to calculate.\n\nVarPad is in your Start menu.\nRemove it any time through Windows Settings → Apps.";
                install.Text = "Open VarPad";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not install", MessageBoxButtons.OK, MessageBoxIcon.Error); install.Text = "Try again"; }
            finally { install.Enabled = true; }
        }
    }
}
