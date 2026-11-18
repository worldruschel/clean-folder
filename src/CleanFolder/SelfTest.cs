using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CleanFolder
{
    /// <summary>
    /// Служебная самопроверка без окна: CleanFolder.exe --selftest &lt;пустая-папка-для-теста&gt; &lt;отчёт&gt;.
    /// Создаёт тестовые файлы, проверяет правила, исключения, отметки и фильтры списка, удаление насовсем.
    /// </summary>
    static class SelfTest
    {
        public static int Run(string dir, string report)
        {
            var log = new StringBuilder();
            int fails = 0;
            void Check(bool ok, string what) { log.AppendLine((ok ? "OK   " : "FAIL ") + what); if (!ok) fails++; }

            try
            {
                dir = Path.GetFullPath(dir);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                string P(string rel) => Path.Combine(dir, rel);
                void W(string rel, int size, DateTime? mod = null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(P(rel)));
                    var data = new byte[size];
                    new Random(rel.GetHashCode()).NextBytes(data);
                    File.WriteAllBytes(P(rel), data);
                    if (mod.HasValue) File.SetLastWriteTime(P(rel), mod.Value);
                }
                var old = DateTime.Now.AddDays(-400);
                W("a.tmp", 100); W("b.bak", 200); W("Docs\\~$x.docx", 10); W("Docs\\empty.txt", 0);
                Directory.CreateDirectory(P("Downloads")); Directory.CreateDirectory(P("Other"));
                W("Photo\\1.jpg", 5000); File.Copy(P("Photo\\1.jpg"), P("Downloads\\1 (1).jpg"));
                W("Keep\\orig.jpg", 7000); File.Copy(P("Keep\\orig.jpg"), P("Other\\copy.jpg"));
                W("Keep\\junk.tmp", 50);
                W("Downloads\\setup.msi", 3000, old);
                Directory.CreateDirectory(P("Empty\\Inner"));
                Directory.CreateDirectory(P("Keep\\EmptyKept"));
                W("x.psd", 999);

                // 1. Обычный анализ
                var r = new Scanner().Scan(dir, new ScanOptions(), null, CancellationToken.None);
                List<Suggestion> Of(ScanResult x, Category c) => x.Suggestions.Where(s => s.Category == c).ToList();
                Check(Of(r, Category.Temp).Count == 4, "временные: a.tmp, b.bak, ~$x.docx, Keep\\junk.tmp");
                Check(Of(r, Category.Duplicate).Count == 2, "дубликаты: 2 копии");
                Check(Of(r, Category.EmptyFile).Count == 1, "пустой файл");
                Check(Of(r, Category.EmptyFolder).Select(s => Path.GetFileName(s.Path)).OrderBy(x => x).SequenceEqual(new[] { "Empty", "EmptyKept" }), "пустые папки – только верхние");
                Check(Of(r, Category.Installer).Count == 1, "старый установщик");

                // 2. Исключения: папка Keep по имени, маска *.psd не влияет на предложения; оригинал в Keep, копия снаружи остаётся предложенной
                var opt = new ScanOptions { Exclusions = new List<string> { "Keep", "*.tmp" } };
                var r2 = new Scanner().Scan(dir, opt, null, CancellationToken.None);
                Check(!r2.Suggestions.Any(s => s.Path.IndexOf("\\Keep\\", StringComparison.OrdinalIgnoreCase) >= 0 || s.Path.EndsWith("\\Keep\\EmptyKept")), "исключение по имени папки");
                Check(!r2.Suggestions.Any(s => s.Path.EndsWith(".tmp")), "исключение по маске *.tmp");
                Check(r2.Suggestions.Any(s => s.Category == Category.Duplicate && s.Path.EndsWith("Other\\copy.jpg")), "копия исключённого оригинала предлагается");
                var opt3 = new ScanOptions { Exclusions = new List<string> { P("Photo") } };
                var r3 = new Scanner().Scan(dir, opt3, null, CancellationToken.None);
                Check(r3.Suggestions.Any(s => s.Category == Category.Duplicate && s.Path.EndsWith("Downloads\\1 (1).jpg")), "исключение по полному пути: дубликат вне папки остаётся");
                var r4 = new Scanner().Scan(dir, new ScanOptions { Temp = false, Installers = false, Duplicates = false }, null, CancellationToken.None);
                Check(!r4.Suggestions.Any(s => s.Category == Category.Temp || s.Category == Category.Installer || s.Category == Category.Duplicate), "выключенные категории не ищутся");

                // 3. Список: отметки, фильтры, сортировка, удаление строк
                using (var form = new Form())
                {
                    var g = new ItemGrid(ItemGrid.Kind.Suggestions) { Dock = DockStyle.Fill };
                    form.Controls.Add(g);
                    form.CreateControl();
                    g.Root = r.Root;
                    g.SetItems(r.Suggestions, checkRecommended: true);
                    Check(g.Checked.Count == r.Suggestions.Count(s => s.Recommended), "рекомендуемое отмечено по умолчанию");
                    g.Filter = s => s.Category == Category.Temp;
                    g.Refilter();
                    Check(g.View.Count == 4 && g.RowCount == 4, "фильтр по категории");
                    g.CheckWhere(_ => true, false);
                    Check(g.CheckedItems().All(s => s.Category != Category.Temp), "снять отметки только у видимых");
                    g.CheckWhere(_ => true, false, visibleOnly: false);
                    Check(g.Checked.Count == 0, "снять все отметки");
                    g.Filter = null; g.Refilter();
                    var empty = r.Suggestions.First(s => s.Category == Category.EmptyFolder && s.Path.EndsWith("\\Empty"));
                    g.Remove(new[] { P("Keep") });
                    Check(!g.All.Any(s => s.Path.StartsWith(P("Keep") + "\\")), "удаление строк внутри удалённой папки");
                    Check(g.All.Contains(empty), "прочие строки на месте");
                }

                // 4. Удаление насовсем (Корзину в тесте не трогаем)
                var toDelete = Of(r, Category.Temp).Where(s => !s.Path.Contains("\\Keep\\")).Concat(Of(r, Category.EmptyFolder).Where(s => s.Path.EndsWith("\\Empty"))).ToList();
                var o = Deleter.Delete(toDelete, toRecycleBin: false);
                Check(o.Deleted.Count == toDelete.Count && toDelete.All(s => !File.Exists(s.Path) && !Directory.Exists(s.Path)), "удаление файлов и пустой папки");
                File.WriteAllText(P("Photo\\1.jpg"), "changed");
                var dup = Of(r, Category.Duplicate).First(s => s.Path.EndsWith("1 (1).jpg"));
                File.WriteAllText(dup.Path, "changed after scan");
                var o2 = Deleter.Delete(new List<Suggestion> { dup }, toRecycleBin: false);
                Check(o2.Deleted.Count == 0 && File.Exists(dup.Path), "изменённый после анализа файл не удаляется");

                // 5. Типы и обновление
                Check(Scanner.TypeOf(".MP4") == "Видео" && Scanner.TypeOf(".xyz") == "Прочее", "группы типов");
                Check(Updater.Current.Major >= 2, "версия программы " + Updater.Current);
            }
            catch (Exception ex) { Check(false, "исключение: " + ex); }

            log.AppendLine(fails == 0 ? "ALL PASSED" : $"FAILED: {fails}");
            File.WriteAllText(report, log.ToString(), new UTF8Encoding(false));
            return fails == 0 ? 0 : 1;
        }
    }
}
