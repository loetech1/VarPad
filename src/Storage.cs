using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Serialization;

namespace VarPad
{
    public sealed class Note
    {
        public string Id = Guid.NewGuid().ToString("N");
        public string Title = "Scratchpad";
        public string Text = "";
        public double Left = -1;
        public double Top = -1;
        public double Width = 580;
        public double Height = 570;
        public double FontSize = 17;
        public double BackgroundOpacity = 0.94;
        public bool Pinned;
        public bool WrapText;
        public bool Degrees = true;
        public bool WasOpen = true;
        public string Theme = "Green";
        public DateTime Modified = DateTime.UtcNow;
    }

    public sealed class NoteStore
    {
        public readonly string DirectoryPath;
        public readonly List<string> Warnings = new List<string>();
        private readonly XmlSerializer serializer = new XmlSerializer(typeof(Note));
        public NoteStore(string path) { DirectoryPath = Path.GetFullPath(path); Directory.CreateDirectory(DirectoryPath); }

        public void ImportLegacy(string legacyDirectory)
        {
            string marker = Path.Combine(DirectoryPath, ".legacy-imported");
            if (File.Exists(marker)) return;
            try
            {
                if (Directory.Exists(legacyDirectory))
                {
                    foreach (string file in Directory.GetFiles(legacyDirectory, "*.xml"))
                    {
                        Guid id;
                        if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out id)) continue;
                        string target = Path.Combine(DirectoryPath, Path.GetFileName(file));
                        if (File.Exists(target)) continue;
                        File.Copy(file, target, false);
                        if (File.Exists(file + ".bak")) File.Copy(file + ".bak", target + ".bak", false);
                    }
                }
                File.WriteAllText(marker, "Legacy notes copied. Originals were left untouched.");
            }
            catch (Exception ex) { Warnings.Add("Could not finish importing StickierCalc notes: " + ex.Message + ". Your original notes have been kept."); }
        }

        public List<Note> Load()
        {
            List<Note> notes = new List<Note>();
            foreach (string file in Directory.GetFiles(DirectoryPath, "*.xml"))
            {
                try { notes.Add(Read(file)); }
                catch (Exception ex)
                {
                    try
                    {
                        if (!File.Exists(file + ".bak")) throw new InvalidDataException("No backup");
                        notes.Add(Read(file + ".bak"));
                        Warnings.Add("Recovered a note from its backup. The original file is still in your notes folder.");
                    }
                    catch { Warnings.Add("Could not read " + Path.GetFileName(file) + ": " + ex.Message + ". The file has been kept."); }
                }
            }
            return notes.OrderByDescending(n => n.Modified).ToList();
        }

        private Note Read(string path)
        {
            XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 };
            Note note;
            using (XmlReader reader = XmlReader.Create(path, settings)) note = (Note)serializer.Deserialize(reader);
            Guid id;
            if (!Guid.TryParseExact(note.Id, "N", out id) || Path.GetFileNameWithoutExtension(path).Replace(".xml", "") != note.Id) throw new InvalidDataException("Invalid note identifier");
            note.Text = note.Text ?? "";
            note.Title = String.IsNullOrWhiteSpace(note.Title) ? "Scratchpad" : note.Title;
            note.Width = Clamp(note.Width, 400, 2400, 580);
            note.Height = Clamp(note.Height, 320, 1800, 570);
            note.FontSize = Clamp(note.FontSize, 12, 28, 17);
            note.BackgroundOpacity = Clamp(note.BackgroundOpacity, .65, 1, .94);
            note.Left = Clamp(note.Left, -20000, 20000, -1);
            note.Top = Clamp(note.Top, -20000, 20000, -1);
            return note;
        }

        public void Save(Note note)
        {
            Guid id;
            if (!Guid.TryParseExact(note.Id, "N", out id)) throw new InvalidDataException("Invalid note identifier");
            string path = Path.Combine(DirectoryPath, note.Id + ".xml");
            string temp = path + ".tmp";
            note.Modified = DateTime.UtcNow;
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { serializer.Serialize(stream, note); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }

        public void Delete(Note note)
        {
            Guid id;
            if (!Guid.TryParseExact(note.Id, "N", out id)) throw new InvalidDataException("Invalid note identifier");
            string path = Path.Combine(DirectoryPath, note.Id + ".xml");
            // Delete the primary last so a failed cleanup leaves the saved note available.
            File.Delete(path + ".tmp");
            File.Delete(path + ".bak");
            File.Delete(path);
        }

        private static double Clamp(double value, double min, double max, double fallback)
        { return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value)); }
    }
}
