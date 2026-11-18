using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace CleanFolder
{
    public enum Category { Temp, Duplicate, EmptyFile, EmptyFolder, Installer, OldLarge }

    public sealed class Suggestion
    {
        public string Path;
        public bool IsFolder;
        public long Size;
        public DateTime Modified;
        public Category Category;
        public string Reason;
        public bool Recommended;   // отмечено по умолчанию (безопасно удалять)
    }

    /// <summary>Что искать и какие пороги использовать (из настроек).</summary>
    public sealed class ScanOptions
    {
        public bool Temp = true, Duplicates = true, EmptyFiles = true, EmptyFolders = true, Installers = true, OldLarge = true;
        public long LargeSize = 100L * 1024 * 1024;
        public int LargeAgeDays = 180, InstallerAgeDays = 30, LogAgeDays = 30;
        public long DupMinSize = 1;
        public List<string> Exclusions = new List<string>();

        string root;
        List<string> paths, names;
        List<Regex> masks;

        /// <summary>Готовит правила исключений для конкретной корневой папки.</summary>
        public void Prepare(string rootFolder)
        {
            root = rootFolder.TrimEnd('\\');
            paths = new List<string>(); names = new List<string>(); masks = new List<Regex>();
            foreach (var raw in Exclusions ?? new List<string>())
            {
                var x = (raw ?? "").Trim().Trim('"');
                if (x.Length == 0 || x.StartsWith("#")) continue;
                if (x.Contains("\\") || x.Contains(":")) paths.Add(x.TrimEnd('\\'));
                else if (x.IndexOfAny(new[] { '*', '?' }) >= 0)
                    masks.Add(new Regex("^" + Regex.Escape(x).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
                else names.Add(x);
            }
        }

        /// <summary>Исключён ли путь из предложений: полный путь (и всё внутри), имя папки или файла, маска вида *.psd.</summary>
        public bool IsExcluded(string path)
        {
            if (paths == null) return false;
            foreach (var p in paths)
                if (path.Equals(p, StringComparison.OrdinalIgnoreCase) || path.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            if (names.Count == 0 && masks.Count == 0) return false;
            var rel = path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) ? path.Substring(root.Length + 1) : System.IO.Path.GetFileName(path);
            foreach (var seg in rel.Split('\\'))
            {
                foreach (var n in names) if (seg.Equals(n, StringComparison.OrdinalIgnoreCase)) return true;
                foreach (var m in masks) if (m.IsMatch(seg)) return true;
            }
            return false;
        }
    }

    public sealed class FileItem
    {
        public string Path;
        public long Size;
        public DateTime Modified;
        public FileAttributes Attributes;
        public string Name => System.IO.Path.GetFileName(Path);
        public string Ext => System.IO.Path.GetExtension(Path).ToLowerInvariant();
    }

    public sealed class TypeStat
    {
        public string Name;
        public int Count;
        public long Size;
    }

    public sealed class ScanResult
    {
        public string Root;
        public int Files;
        public int Folders;
        public long TotalSize;
        public int Errors;
        public TimeSpan Elapsed;
        public List<FileItem> Largest = new List<FileItem>();
        public List<Suggestion> Suggestions = new List<Suggestion>();
        public List<TypeStat> Types = new List<TypeStat>();
    }

    public static class Categories
    {
        public static string Title(Category c)
        {
            switch (c)
            {
                case Category.Temp: return "Временные файлы";
                case Category.Duplicate: return "Дубликаты";
                case Category.EmptyFile: return "Пустые файлы";
                case Category.EmptyFolder: return "Пустые папки";
                case Category.Installer: return "Установщики";
                case Category.OldLarge: return "Большие и старые";
                default: return c.ToString();
            }
        }

        public static string Advice(Category c)
        {
            switch (c)
            {
                case Category.Temp: return "Временные, резервные и служебные файлы программ. Их можно удалять смело – программы создадут их заново, если понадобится.";
                case Category.Duplicate: return "Точные копии файлов (совпадает содержимое). Оригинал – самый старый экземпляр – остаётся на месте, удаляются только копии.";
                case Category.EmptyFile: return "Файлы размером 0 байт – в них ничего нет. Обычно это следы неудачного сохранения или загрузки.";
                case Category.EmptyFolder: return "Папки, в которых нет ни одного файла. Только загромождают структуру.";
                case Category.Installer: return "Установочные файлы программ и образы дисков. После установки обычно не нужны, но проверьте, не храните ли вы их специально. По умолчанию не отмечены.";
                case Category.OldLarge: return "Крупные файлы, которые давно не менялись. Решайте сами: возможно, их лучше перенести в архив или на внешний диск. По умолчанию не отмечены.";
                default: return "";
            }
        }
    }

    public static class Format
    {
        public static string Size(long bytes)
        {
            string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return u == 0 ? $"{bytes} Б" : $"{v:0.#} {units[u]}";
        }

        public static string Count(int n, string one, string few, string many)
        {
            int n10 = n % 10, n100 = n % 100;
            string w = (n10 == 1 && n100 != 11) ? one : (n10 >= 2 && n10 <= 4 && (n100 < 10 || n100 >= 20)) ? few : many;
            return $"{n:N0} {w}";
        }

        public static string Duration(TimeSpan t) =>
            t.TotalSeconds < 1 ? "меньше секунды" : t.TotalSeconds < 60 ? $"{t.TotalSeconds:0.#} с" : $"{(int)t.TotalMinutes} мин {t.Seconds} с";

        public static string Files(int n) => Count(n, "файл", "файла", "файлов");
        public static string Items(int n) => Count(n, "объект", "объекта", "объектов");
    }

    public sealed class Scanner
    {
        static readonly HashSet<string> TempExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".tmp", ".temp", ".bak", ".old", ".dmp", ".chk", ".gid", ".crdownload", ".part", ".partial", ".download", ".~tmp", ".$$$" };
        static readonly HashSet<string> JunkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "thumbs.db", "ehthumbs.db", "ehthumbs_vista.db", ".ds_store" };
        static readonly HashSet<string> KeepEmpty = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "__init__.py", "py.typed", "lock", "placeholder" };
        static readonly HashSet<string> InstallerExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".iso", ".img" };
        static readonly string[] SkipDirs = { "\\.git\\", "\\.svn\\", "\\.hg\\", "\\node_modules\\", "\\$recycle.bin\\", "\\system volume information\\" };
        static readonly Regex SetupName = new Regex(@"setup|install|инстал|установ", RegexOptions.IgnoreCase | RegexOptions.Compiled);


        static readonly (string name, string[] ext)[] TypeGroups =
        {
            ("Видео", new[] { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpg", ".mpeg", ".3gp" }),
            ("Фото и картинки", new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".heic", ".webp", ".tif", ".tiff", ".raw", ".cr2", ".nef", ".arw", ".dng", ".svg", ".psd", ".ico" }),
            ("Музыка и звук", new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a", ".wma", ".opus" }),
            ("Документы", new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".odp", ".csv", ".md", ".djvu", ".epub", ".fb2" }),
            ("Архивы", new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".cab" }),
            ("Программы и образы", new[] { ".exe", ".msi", ".msix", ".dll", ".iso", ".img", ".apk", ".appx" }),
        };

        /// <summary>К какой группе («Видео», «Документы»…) относится расширение.</summary>
        public static string TypeOf(string ext)
        {
            var e = (ext ?? "").ToLowerInvariant();
            foreach (var g in TypeGroups) if (Array.IndexOf(g.ext, e) >= 0) return g.name;
            return "Прочее";
        }

        ScanOptions opt = new ScanOptions();

        public ScanResult Scan(string root, IProgress<string> progress, CancellationToken ct) => Scan(root, new ScanOptions(), progress, ct);

        public ScanResult Scan(string root, ScanOptions options, IProgress<string> progress, CancellationToken ct)
        {
            opt = options ?? new ScanOptions();
            opt.Prepare(root);
            var started = DateTime.Now;
            var r = new ScanResult { Root = root };
            var files = new List<FileItem>();
            var dirs = new List<string>();
            var hasContent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lastReport = DateTime.MinValue;

            // 1. Обход папки (без перехода по ссылкам-точкам соединения)
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var d = stack.Pop();
                List<FileSystemInfo> entries;
                try { entries = d.EnumerateFileSystemInfos().ToList(); }
                catch { r.Errors++; hasContent.Add(d.FullName); continue; }

                foreach (var e in entries)
                {
                    if (e is DirectoryInfo sub)
                    {
                        if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) { hasContent.Add(d.FullName); continue; }
                        dirs.Add(sub.FullName);
                        r.Folders++;
                        stack.Push(sub);
                    }
                    else if (e is FileInfo f)
                    {
                        try
                        {
                            files.Add(new FileItem { Path = f.FullName, Size = f.Length, Modified = f.LastWriteTime, Attributes = f.Attributes });
                            r.TotalSize += f.Length;
                            hasContent.Add(d.FullName);
                        }
                        catch { r.Errors++; hasContent.Add(d.FullName); }
                    }
                }

                if ((DateTime.Now - lastReport).TotalMilliseconds > 150)
                {
                    lastReport = DateTime.Now;
                    progress?.Report($"Просматриваю: {files.Count:N0} файлов, {Format.Size(r.TotalSize)}… {d.FullName}");
                }
            }
            r.Files = files.Count;

            // 2. Статистика и самые крупные файлы
            r.Largest = files.OrderByDescending(f => f.Size).Take(1000).ToList();
            var extToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in TypeGroups) foreach (var x in g.ext) if (!extToGroup.ContainsKey(x)) extToGroup[x] = g.name;
            r.Types = files.GroupBy(f => extToGroup.TryGetValue(f.Ext, out var n) ? n : "Прочее")
                .Select(g => new TypeStat { Name = g.Key, Count = g.Count(), Size = g.Sum(f => f.Size) })
                .OrderByDescending(t => t.Size).ToList();

            // 3. Правила для отдельных файлов
            progress?.Report("Ищу мусор…");
            var now = DateTime.Now;
            var suggested = new HashSet<FileItem>();
            var candidates = new List<FileItem>();
            var excluded = new HashSet<FileItem>();
            foreach (var f in files)
            {
                if (IsProtected(f)) continue;
                if (opt.IsExcluded(f.Path)) { excluded.Add(f); candidates.Add(f); continue; }
                var s = Classify(f, now);
                if (s != null) { r.Suggestions.Add(s); suggested.Add(f); }
                else candidates.Add(f);
            }

            // 4. Дубликаты (исключённые файлы участвуют как оригиналы, но сами не предлагаются)
            if (opt.Duplicates) FindDuplicates(candidates, excluded, r, progress, ct);

            // 5. Пустые папки (показываем только верхние, вложенные удалятся вместе с ними)
            var nonEmpty = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rootFull = Path.GetFullPath(root).TrimEnd('\\');
            foreach (var p in hasContent)
            {
                var cur = p;
                while (cur != null && cur.Length > rootFull.Length && nonEmpty.Add(cur))
                    cur = Path.GetDirectoryName(cur);
            }
            var empty = new HashSet<string>(dirs.Where(d => !nonEmpty.Contains(d) && !InSkipDir(d + "\\")), StringComparer.OrdinalIgnoreCase);
            foreach (var d in opt.EmptyFolders ? empty : new HashSet<string>())
            {
                if (opt.IsExcluded(d)) continue;
                var parent = Path.GetDirectoryName(d);
                if (empty.Contains(parent)) continue;
                DateTime mod;
                try { mod = Directory.GetLastWriteTime(d); } catch { mod = DateTime.MinValue; }
                r.Suggestions.Add(new Suggestion { Path = d, IsFolder = true, Category = Category.EmptyFolder, Modified = mod, Reason = "Пустая папка – в ней нет файлов", Recommended = true });
            }

            r.Suggestions = r.Suggestions.OrderBy(s => s.Category).ThenByDescending(s => s.Size).ToList();
            r.Elapsed = DateTime.Now - started;
            return r;
        }

        static bool InSkipDir(string path)
        {
            foreach (var s in SkipDirs) if (path.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static bool IsProtected(FileItem f) =>
            (f.Attributes & FileAttributes.System) != 0 && !JunkNames.Contains(f.Name)
            || InSkipDir(f.Path);

        Suggestion Classify(FileItem f, DateTime now)
        {
            var name = f.Name;
            var ext = f.Ext;
            int age = (int)(now - f.Modified).TotalDays;
            Suggestion S(Category c, string reason, bool rec) =>
                new Suggestion { Path = f.Path, Size = f.Size, Modified = f.Modified, Category = c, Reason = reason, Recommended = rec };

            if (opt.Temp)
            {
                if (JunkNames.Contains(name)) return S(Category.Temp, "Кэш эскизов – Windows создаст его заново", true);
                if (name.StartsWith("~$")) return S(Category.Temp, "Временный файл Office (остался после закрытия документа)", true);
                if (TempExt.Contains(ext)) return S(Category.Temp, $"Временный или резервный файл ({ext})", true);
                if (ext == ".log" && age > opt.LogAgeDays) return S(Category.Temp, $"Старый журнал программы, не менялся {age} дн.", true);
            }

            if (opt.EmptyFiles && f.Size == 0 && !name.StartsWith(".") && !KeepEmpty.Contains(name))
                return S(Category.EmptyFile, "Пустой файл (0 байт)", true);

            if (opt.Installers && age > opt.InstallerAgeDays && (InstallerExt.Contains(ext) || (ext == ".exe" && SetupName.IsMatch(name))))
                return S(Category.Installer, $"Установщик или образ диска, лежит {age} дн. Если программа уже установлена – не нужен", false);

            if (opt.OldLarge && f.Size >= opt.LargeSize && age > opt.LargeAgeDays)
                return S(Category.OldLarge, $"Крупный файл, не менялся {age / 30} мес. Перенесите в архив или удалите, если не нужен", false);

            return null;
        }

        void FindDuplicates(List<FileItem> candidates, HashSet<FileItem> excluded, ScanResult r, IProgress<string> progress, CancellationToken ct)
        {
            var bySize = candidates.Where(f => f.Size > 0 && f.Size >= opt.DupMinSize).GroupBy(f => f.Size).Where(g => g.Count() > 1).ToList();
            int total = bySize.Sum(g => g.Count()), done = 0;
            var lastReport = DateTime.MinValue;

            foreach (var sizeGroup in bySize)
            {
                ct.ThrowIfCancellationRequested();
                // сначала начало файла (быстро), затем весь файл
                var byHead = new Dictionary<string, List<FileItem>>();
                foreach (var f in sizeGroup)
                {
                    ct.ThrowIfCancellationRequested();
                    done++;
                    if ((DateTime.Now - lastReport).TotalMilliseconds > 150)
                    {
                        lastReport = DateTime.Now;
                        progress?.Report($"Сравниваю похожие файлы: {done:N0} из {total:N0}");
                    }
                    var h = Hash(f.Path, 64 * 1024, ct);
                    if (h == null) continue;
                    if (!byHead.TryGetValue(h, out var list)) byHead[h] = list = new List<FileItem>();
                    list.Add(f);
                }

                foreach (var headGroup in byHead.Values.Where(l => l.Count > 1))
                {
                    List<List<FileItem>> groups;
                    if (sizeGroup.Key <= 64 * 1024) groups = new List<List<FileItem>> { headGroup };
                    else
                    {
                        var byFull = new Dictionary<string, List<FileItem>>();
                        foreach (var f in headGroup)
                        {
                            progress?.Report($"Сравниваю содержимое: {f.Name} ({Format.Size(f.Size)})");
                            var h = Hash(f.Path, long.MaxValue, ct);
                            if (h == null) continue;
                            if (!byFull.TryGetValue(h, out var list)) byFull[h] = list = new List<FileItem>();
                            list.Add(f);
                        }
                        groups = byFull.Values.Where(l => l.Count > 1).ToList();
                    }

                    foreach (var g in groups)
                    {
                        var original = g.OrderBy(f => excluded.Contains(f) ? 0 : 1).ThenBy(f => f.Modified).ThenBy(f => f.Path.Length).First();
                        var rel = RelativeFolder(r.Root, original.Path);
                        foreach (var copy in g.Where(f => f != original && !excluded.Contains(f)))
                            r.Suggestions.Add(new Suggestion
                            {
                                Path = copy.Path, Size = copy.Size, Modified = copy.Modified, Category = Category.Duplicate, Recommended = true,
                                Reason = $"Копия файла «{original.Name}» из папки {rel}"
                            });
                    }
                }
            }
        }

        public static string RelativeFolder(string root, string path)
        {
            var dir = Path.GetDirectoryName(path) ?? "";
            var rootFull = root.TrimEnd('\\');
            if (dir.Equals(rootFull, StringComparison.OrdinalIgnoreCase)) return "(корень)";
            if (dir.StartsWith(rootFull + "\\", StringComparison.OrdinalIgnoreCase)) return dir.Substring(rootFull.Length + 1);
            return dir;
        }

        static string Hash(string path, long limit, CancellationToken ct)
        {
            try
            {
                using (var md5 = MD5.Create())
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
                {
                    var buf = new byte[1 << 16];
                    long left = limit;
                    int n;
                    while (left > 0 && (n = fs.Read(buf, 0, (int)Math.Min(buf.Length, left))) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        md5.TransformBlock(buf, 0, n, null, 0);
                        left -= n;
                    }
                    md5.TransformFinalBlock(buf, 0, 0);
                    return Convert.ToBase64String(md5.Hash);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { return null; }
        }
    }
}
