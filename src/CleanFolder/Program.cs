using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CleanFolder
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            // Служебный режим для проверки без окна: CleanFolder.exe --report <папка> <файл-отчёта>
            if (args.Length == 3 && args[0] == "--report") return Report(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--selftest") return SelfTest.Run(args[1], args[2]);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = Math.Max(1f, g.DpiX / 96f); } catch { }

            // Служебный режим: снимки окна для проверки оформления. CleanFolder.exe --shot <папка> <префикс-файлов>
            if (args.Length == 3 && args[0] == "--shot")
            {
                AppSettings.ReadOnly = true;
                Application.Run(new MainForm(args[1]) { ShotPrefix = args[2] });
                return 0;
            }

            // Папка из Проводника («Навести порядок») или из командной строки
            var folder = args.FirstOrDefault(a => !a.StartsWith("--"));
            if (folder != null)
            {
                folder = folder.Trim().Trim('"');
                if (folder.Length == 2 && folder[1] == ':') folder += "\\";
            }
            Application.Run(new MainForm(folder));
            return 0;
        }

        static int Report(string folder, string output)
        {
            var r = new Scanner().Scan(Path.GetFullPath(folder), null, CancellationToken.None);
            var sb = new StringBuilder();
            sb.AppendLine($"Files={r.Files} Folders={r.Folders} Size={r.TotalSize} Errors={r.Errors}");
            foreach (var t in r.Types) sb.AppendLine($"TYPE {t.Name}: {t.Count} / {t.Size}");
            foreach (var s in r.Suggestions)
                sb.AppendLine($"{s.Category}\t{(s.Recommended ? "+" : "-")}\t{Scanner.RelativeFolder(r.Root, s.Path)}\\{Path.GetFileName(s.Path)}\t{s.Size}\t{s.Reason}");
            File.WriteAllText(output, sb.ToString(), new UTF8Encoding(false));
            return 0;
        }
    }
}
