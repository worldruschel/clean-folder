using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CleanFolder
{
    /// <summary>Удаление в Корзину (через Shell) или насовсем, с проверкой, что файл не изменился после анализа.</summary>
    public static class Deleter
    {
        public sealed class Outcome
        {
            public List<Suggestion> Deleted = new List<Suggestion>();
            public Dictionary<Suggestion, string> Failed = new Dictionary<Suggestion, string>();
            public long Freed => Deleted.Sum(s => s.Size);
        }

        public static Outcome Delete(IList<Suggestion> items, bool toRecycleBin, IProgress<int> progress = null)
        {
            var o = new Outcome();
            var ready = new List<Suggestion>();
            foreach (var s in items)
            {
                var problem = Check(s);
                if (problem != null) o.Failed[s] = problem; else ready.Add(s);
            }

            const int chunk = 100;
            for (int i = 0; i < ready.Count; i += chunk)
            {
                var part = ready.Skip(i).Take(chunk).ToList();
                if (toRecycleBin) Recycle(part.Select(s => s.Path));
                else foreach (var s in part) DeleteForever(s, o);

                foreach (var s in part)
                {
                    if (o.Failed.ContainsKey(s)) continue;
                    if (Exists(s)) o.Failed[s] = "Не удалось удалить: файл занят другой программой или нет прав";
                    else o.Deleted.Add(s);
                }
                progress?.Report(Math.Min(i + chunk, ready.Count) * 100 / Math.Max(1, ready.Count));
            }
            return o;
        }

        static bool Exists(Suggestion s) => s.IsFolder ? Directory.Exists(s.Path) : File.Exists(s.Path);

        static string Check(Suggestion s)
        {
            try
            {
                if (s.IsFolder)
                {
                    if (!Directory.Exists(s.Path)) return "Папки уже нет";
                    if (Directory.EnumerateFiles(s.Path, "*", SearchOption.AllDirectories).Any()) return "В папке появились файлы – пропущена";
                    return null;
                }
                var fi = new FileInfo(s.Path);
                if (!fi.Exists) return "Файла уже нет";
                if (fi.Length != s.Size || Math.Abs((fi.LastWriteTime - s.Modified).TotalSeconds) > 2)
                    return "Файл изменился после анализа – пропущен, запустите анализ заново";
                return null;
            }
            catch (Exception ex) { return "Ошибка проверки: " + ex.Message; }
        }

        static void DeleteForever(Suggestion s, Outcome o)
        {
            try
            {
                if (s.IsFolder) Directory.Delete(s.Path, true);
                else
                {
                    var attr = File.GetAttributes(s.Path);
                    if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(s.Path, attr & ~FileAttributes.ReadOnly);
                    File.Delete(s.Path);
                }
            }
            catch (Exception ex) { o.Failed[s] = "Не удалось удалить: " + ex.Message; }
        }

        // ---- Корзина через SHFileOperation (сборка x64, поэтому выравнивание по умолчанию) ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        const uint FO_DELETE = 3;
        const ushort FOF_SILENT = 0x4, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

        static void Recycle(IEnumerable<string> paths)
        {
            var op = new SHFILEOPSTRUCT
            {
                wFunc = FO_DELETE,
                pFrom = string.Join("\0", paths) + "\0\0",
                fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
            };
            SHFileOperation(ref op);
        }
    }
}
