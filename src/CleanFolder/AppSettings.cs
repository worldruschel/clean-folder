using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CleanFolder
{
    /// <summary>Настройки программы. Хранятся в реестре: HKCU\Software\CleanFolder (удаляются вместе с программой).</summary>
    public sealed class AppSettings
    {
        public const string RegKey = @"Software\CleanFolder";

        // Что искать
        public bool FindTemp = true, FindDuplicates = true, FindEmptyFiles = true, FindEmptyFolders = true, FindInstallers = true, FindOldLarge = true;
        // Пороги
        public int LargeMb = 100, LargeAgeDays = 180, InstallerAgeDays = 30, LogAgeDays = 30, DupMinKb = 0;
        public List<string> Exclusions = new List<string>();
        // Удаление
        public bool RecycleBin = true, ConfirmSingle = false;
        // Вид
        public ThemeMode Theme = ThemeMode.System;
        // Обновления
        public bool AutoCheckUpdates = true, AutoInstallUpdates = true;
        // Проводник
        public bool ContextMenu = true, ContextMenuInitialized;
        // Состояние
        public string LastFolder;
        public List<string> Recent = new List<string>();
        public Rectangle Bounds;
        public bool Maximized;

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RegKey))
                {
                    if (k == null) return s;
                    bool B(string n, bool d) => k.GetValue(n) is int v ? v != 0 : d;
                    int I(string n, int d) => k.GetValue(n) is int v ? v : d;
                    List<string> L(string n) => (k.GetValue(n) as string[])?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();

                    s.FindTemp = B("FindTemp", true); s.FindDuplicates = B("FindDuplicates", true); s.FindEmptyFiles = B("FindEmptyFiles", true);
                    s.FindEmptyFolders = B("FindEmptyFolders", true); s.FindInstallers = B("FindInstallers", true); s.FindOldLarge = B("FindOldLarge", true);
                    s.LargeMb = Clamp(I("LargeMb", 100), 1, 1000000); s.LargeAgeDays = Clamp(I("LargeAgeDays", 180), 0, 36500);
                    s.InstallerAgeDays = Clamp(I("InstallerAgeDays", 30), 0, 36500); s.LogAgeDays = Clamp(I("LogAgeDays", 30), 0, 36500);
                    s.DupMinKb = Clamp(I("DupMinKb", 0), 0, 10000000);
                    s.Exclusions = L("Exclusions");
                    s.RecycleBin = B("RecycleBin", true); s.ConfirmSingle = B("ConfirmSingle", false);
                    s.Theme = (ThemeMode)Clamp(I("Theme", 0), 0, 2);
                    s.AutoCheckUpdates = B("AutoCheckUpdates", true); s.AutoInstallUpdates = B("AutoInstallUpdates", true);
                    s.ContextMenu = B("ContextMenu", true); s.ContextMenuInitialized = B("ContextMenuInitialized", false);
                    s.LastFolder = k.GetValue("LastFolder") as string;
                    s.Recent = L("Recent");
                    if (k.GetValue("Bounds") is string b)
                    {
                        var p = b.Split(',').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
                        if (p.Length == 4) s.Bounds = new Rectangle(p[0], p[1], p[2], p[3]);
                    }
                    s.Maximized = B("Maximized", false);
                }
            }
            catch { }
            if (s.Recent.Count == 0 && !string.IsNullOrEmpty(s.LastFolder)) s.Recent.Add(s.LastFolder);
            return s;
        }

        static int Clamp(int v, int min, int max) => Math.Max(min, Math.Min(max, v));

        /// <summary>Не записывать настройки (служебный режим снимков экрана).</summary>
        public static bool ReadOnly;

        public void Save()
        {
            if (ReadOnly) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
                {
                    void B(string n, bool v) => k.SetValue(n, v ? 1 : 0, RegistryValueKind.DWord);
                    void I(string n, int v) => k.SetValue(n, v, RegistryValueKind.DWord);
                    B("FindTemp", FindTemp); B("FindDuplicates", FindDuplicates); B("FindEmptyFiles", FindEmptyFiles);
                    B("FindEmptyFolders", FindEmptyFolders); B("FindInstallers", FindInstallers); B("FindOldLarge", FindOldLarge);
                    I("LargeMb", LargeMb); I("LargeAgeDays", LargeAgeDays); I("InstallerAgeDays", InstallerAgeDays); I("LogAgeDays", LogAgeDays); I("DupMinKb", DupMinKb);
                    k.SetValue("Exclusions", Exclusions.ToArray(), RegistryValueKind.MultiString);
                    B("RecycleBin", RecycleBin); B("ConfirmSingle", ConfirmSingle);
                    I("Theme", (int)Theme);
                    B("AutoCheckUpdates", AutoCheckUpdates); B("AutoInstallUpdates", AutoInstallUpdates);
                    B("ContextMenu", ContextMenu); B("ContextMenuInitialized", ContextMenuInitialized);
                    if (!string.IsNullOrEmpty(LastFolder)) k.SetValue("LastFolder", LastFolder);
                    k.SetValue("Recent", Recent.Take(10).ToArray(), RegistryValueKind.MultiString);
                    if (Bounds.Width > 0) k.SetValue("Bounds", $"{Bounds.X},{Bounds.Y},{Bounds.Width},{Bounds.Height}");
                    B("Maximized", Maximized);
                }
            }
            catch { }
        }

        public void AddRecent(string folder)
        {
            LastFolder = folder;
            Recent.RemoveAll(x => string.Equals(x, folder, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, folder);
            if (Recent.Count > 10) Recent.RemoveRange(10, Recent.Count - 10);
        }

        public ScanOptions ToScanOptions() => new ScanOptions
        {
            Temp = FindTemp, Duplicates = FindDuplicates, EmptyFiles = FindEmptyFiles, EmptyFolders = FindEmptyFolders,
            Installers = FindInstallers, OldLarge = FindOldLarge,
            LargeSize = (long)LargeMb * 1024 * 1024, LargeAgeDays = LargeAgeDays, InstallerAgeDays = InstallerAgeDays, LogAgeDays = LogAgeDays,
            DupMinSize = Math.Max(1, (long)DupMinKb * 1024),
            Exclusions = new List<string>(Exclusions),
        };

        /// <summary>Отпечаток параметров поиска – чтобы понять, что после изменения настроек нужен новый анализ.</summary>
        public string ScanSignature() => string.Join("|", FindTemp, FindDuplicates, FindEmptyFiles, FindEmptyFolders, FindInstallers, FindOldLarge,
            LargeMb, LargeAgeDays, InstallerAgeDays, LogAgeDays, DupMinKb, string.Join(";", Exclusions));
    }

    /// <summary>Пункт «Навести порядок» в контекстном меню папок Проводника (только для текущего пользователя).</summary>
    public static class ShellMenu
    {
        static readonly string[] Keys =
        {
            @"Software\Classes\Directory\shell\CleanFolder",
            @"Software\Classes\Directory\Background\shell\CleanFolder",
            @"Software\Classes\Drive\shell\CleanFolder",
        };

        public static bool IsInstalled()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Keys[0])) return k != null; } catch { return false; }
        }

        public static void Set(bool enabled)
        {
            var exe = Application.ExecutablePath;
            foreach (var key in Keys)
            {
                try
                {
                    if (!enabled) { Registry.CurrentUser.DeleteSubKeyTree(key, false); continue; }
                    using (var k = Registry.CurrentUser.CreateSubKey(key))
                    {
                        k.SetValue("", "Навести порядок («Чистая папка»)");
                        k.SetValue("Icon", $"\"{exe}\",0");
                    }
                    using (var c = Registry.CurrentUser.CreateSubKey(key + @"\command"))
                        c.SetValue("", $"\"{exe}\" \"%V\"");
                }
                catch { }
            }
        }
    }
}
