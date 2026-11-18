using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CleanFolder
{
    /// <summary>
    /// Обновление из GitHub Releases. Берётся последний релиз, в нём установщик CleanFolder-Setup-X.Y.Z.exe
    /// и рядом обязательно CleanFolder-Setup-X.Y.Z.exe.sha256 – без совпадения хеша обновление не ставится.
    /// Источник можно подменить переменной окружения CLEANFOLDER_UPDATE_SOURCE (URL или локальный JSON) для проверки.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "worldruschel/clean-folder";
        public const string RepoUrl = "https://github.com/" + Repo;
        const string DefaultSource = "https://api.github.com/repos/" + Repo + "/releases/latest";

        public sealed class UpdateInfo
        {
            public Version Version;
            public string InstallerName, InstallerUrl, HashUrl, Notes;
        }

#pragma warning disable 0649
        [DataContract]
        sealed class GhRelease
        {
            [DataMember(Name = "tag_name")] public string TagName;
            [DataMember(Name = "draft")] public bool Draft;
            [DataMember(Name = "prerelease")] public bool Prerelease;
            [DataMember(Name = "body")] public string Body;
            [DataMember(Name = "assets")] public GhAsset[] Assets;
        }

        [DataContract]
        sealed class GhAsset
        {
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "browser_download_url")] public string Url;
        }
#pragma warning restore 0649

        static readonly Regex InstallerName = new Regex(@"^CleanFolder-Setup-[\d.]+\.exe$", RegexOptions.IgnoreCase);
        static readonly HttpClient Http = CreateHttp();

        static HttpClient CreateHttp()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(180) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("CleanFolder/" + Current);
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        public static Version Current
        {
            get
            {
                var v = typeof(Updater).Assembly.GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CleanFolder");

        public static void Log(string text)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(Path.Combine(DataDir, "update.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { }
        }

        static string Source
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("CLEANFOLDER_UPDATE_SOURCE");
                return string.IsNullOrWhiteSpace(env) ? DefaultSource : env;
            }
        }

        static bool IsLocal(string s) => !string.IsNullOrEmpty(s) && !s.StartsWith("http", StringComparison.OrdinalIgnoreCase);

        /// <summary>Есть ли версия новее текущей. null – нет; error – текст ошибки, если проверить не удалось.</summary>
        public static async Task<(UpdateInfo info, string error)> CheckAsync()
        {
            var source = Source;
            try
            {
                string json;
                if (IsLocal(source)) json = File.ReadAllText(source, Encoding.UTF8);
                else
                {
                    var t = Http.GetStringAsync(source);
                    if (await Task.WhenAny(t, Task.Delay(15000)).ConfigureAwait(false) != t) return (null, "сервер не ответил за 15 секунд");
                    json = await t.ConfigureAwait(false);
                }

                GhRelease rel;
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    rel = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(ms);

                if (rel == null || rel.Draft || rel.Prerelease || string.IsNullOrEmpty(rel.TagName)) return (null, null);
                if (!Version.TryParse(rel.TagName.TrimStart('v', 'V'), out var latest)) return (null, null);
                latest = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
                if (latest <= Current) { Log($"Установлена последняя версия ({Current})"); return (null, null); }

                var installer = rel.Assets?.FirstOrDefault(a => a.Name != null && InstallerName.IsMatch(a.Name));
                var hash = installer == null ? null : rel.Assets.FirstOrDefault(a => string.Equals(a.Name, installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
                if (installer == null || hash == null)
                {
                    Log($"Версия {latest}: в релизе нет установщика или .sha256 – пропускаю");
                    return (null, null);
                }
                Log($"Найдена версия {latest} (установлена {Current})");
                return (new UpdateInfo { Version = latest, InstallerName = installer.Name, InstallerUrl = installer.Url, HashUrl = hash.Url, Notes = rel.Body }, null);
            }
            catch (Exception ex)
            {
                var msg = (ex as HttpRequestException)?.InnerException?.Message ?? ex.Message;
                Log("Проверить не удалось – " + msg);
                return (null, msg);
            }
        }

        /// <summary>Скачивает установщик и сверяет SHA-256. Возвращает путь или null.</summary>
        public static async Task<string> DownloadAsync(UpdateInfo u)
        {
            try
            {
                var dir = Path.Combine(DataDir, "updates");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                var target = Path.Combine(dir, u.InstallerName);

                string hashText;
                if (IsLocal(u.InstallerUrl))
                {
                    File.Copy(u.InstallerUrl, target, true);
                    hashText = File.ReadAllText(u.HashUrl);
                }
                else
                {
                    var bytes = await Http.GetByteArrayAsync(u.InstallerUrl).ConfigureAwait(false);
                    File.WriteAllBytes(target, bytes);
                    hashText = await Http.GetStringAsync(u.HashUrl).ConfigureAwait(false);
                }

                var expected = (hashText ?? "").Trim().Split(new[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                string actual;
                using (var sha = SHA256.Create())
                using (var fs = File.OpenRead(target))
                    actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "");

                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    Log($"Версия {u.Version}: контрольная сумма не совпала – не устанавливаю");
                    File.Delete(target);
                    return null;
                }
                Log($"Версия {u.Version}: скачано, контрольная сумма совпала");
                return target;
            }
            catch (Exception ex)
            {
                Log("Скачать не удалось – " + ex.Message);
                return null;
            }
        }

        /// <summary>Тихая установка. relaunch – после установки снова открыть программу.</summary>
        public static bool RunInstaller(string path, Version version, bool relaunch)
        {
            try
            {
                var log = Path.Combine(DataDir, $"install-{version}.log");
                var args = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1{(relaunch ? " /RELAUNCH=1" : "")} /LOG=\"{log}\"";
                Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path) })?.Dispose();
                Log($"Версия {version}: установщик запущен{(relaunch ? " (с перезапуском)" : "")}");
                return true;
            }
            catch (Exception ex)
            {
                Log("Не удалось запустить установщик – " + ex.Message);
                return false;
            }
        }
    }
}
