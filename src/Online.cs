using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Lug
{
    internal sealed class PublicBuild
    {
        internal string Repo, Name, Description;
        public override string ToString() { return Name + "  ·  " + Repo; }
    }

    internal sealed class RemoteAsset
    {
        internal string Name, Version, Digest, MinimumGame;
        internal Uri Url;
        internal AssetOrigin Origin;

        internal static RemoteAsset From(object asset, string repo, string version)
        {
            Uri url;
            if (!Uri.TryCreate(Json.Str(asset, "browser_download_url"), UriKind.Absolute, out url) ||
                url.Scheme != "https" || url.Host != "github.com" || !url.IsDefaultPort || url.UserInfo.Length > 0 ||
                !url.AbsolutePath.StartsWith("/" + repo + "/releases/download/", StringComparison.OrdinalIgnoreCase))
                return null;
            return new RemoteAsset { Name = Json.Str(asset, "name"), Version = version, Url = url, Digest = Json.Str(asset, "digest"),
                Origin = new AssetOrigin { Repository = repo, Asset = Json.Str(asset, "name"), AssetId = Json.Str(asset, "id") } };
        }

        internal void Verify(string file)
        {
            if (string.IsNullOrEmpty(Digest)) return;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
            {
                string actual = "sha256:" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                if (!string.Equals(Digest, actual, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Контрольная сумма скачанного файла не совпала. Попробуй скачать его заново.");
            }
        }
    }

    public sealed class AssetOrigin
    {
        public string Repository { get; set; }
        public string Asset { get; set; }
        public string Tag { get; set; }
        public string ReleaseId { get; set; }
        public string AssetId { get; set; }
        public long Published { get; set; }
        public bool OnlyPackage { get; set; }

        internal bool IsNewerThan(AssetOrigin other)
        {
            if (other == null) return true;
            if (Published != other.Published && Published > 0 && other.Published > 0) return Published > other.Published;
            long current, prior;
            if (long.TryParse(ReleaseId, out current) && long.TryParse(other.ReleaseId, out prior) && current != prior) return current > prior;
            return ReleaseId == other.ReleaseId && long.TryParse(AssetId, out current) && long.TryParse(other.AssetId, out prior) && current > prior;
        }
    }

    internal sealed partial class Sources
    {
        internal const string BuildTopic = "stardew-lug-pack";
        private const string SmapiRepo = "Pathoschild/SMAPI";

        internal async Task<List<PublicBuild>> DiscoverBuilds(string term, int page)
        {
            string safe = Regex.Replace(term ?? "", @"[^\p{L}\p{N}_ .-]", " ").Trim();
            string query = "topic:" + BuildTopic + (safe.Length == 0 ? "" : " " + safe);
            var data = await Request("https://api.github.com/search/repositories?q=" + Uri.EscapeDataString(query) +
                "&sort=updated&order=desc&per_page=24&page=" + (page + 1)).ConfigureAwait(false);
            return Json.List(Json.Get(data, "items")).Select(item => new PublicBuild
            {
                Repo = GitRepo(Json.Str(item, "full_name")),
                Name = Json.Str(item, "name"),
                Description = Json.Str(item, "description")
            }).Where(item => item.Repo != null).ToList();
        }

        internal async Task<List<RemoteAsset>> PublicBuildFiles(string target)
        {
            Uri address;
            string repo = GitRepo(target);
            if (repo == null) throw new IOException("Вставь адрес публичного репозитория GitHub или ссылку на его релиз.");
            string endpoint = "/releases/latest";
            string specificFile = null;
            if (Uri.TryCreate(target, UriKind.Absolute, out address))
            {
                var pieces = address.AbsolutePath.Trim('/').Split('/');
                if (pieces.Length >= 4 && pieces[2] == "releases" && (pieces[3] == "tag" || pieces[3] == "download") && pieces.Length >= 5)
                    endpoint = "/releases/tags/" + Uri.EscapeDataString(Uri.UnescapeDataString(pieces[4]));
                if (pieces.Length == 6 && pieces[2] == "releases" && pieces[3] == "download")
                    specificFile = Uri.UnescapeDataString(pieces[5]);
            }
            var release = await Request("https://api.github.com/repos/" + repo + endpoint).ConfigureAwait(false);
            var packs = ReleaseAssets(release, repo).Where(file => file.Name.EndsWith(".lugpack.zip", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var pack in packs) pack.Origin.OnlyPackage = packs.Count == 1;
            return packs.Where(file => specificFile == null || string.Equals(file.Name, specificFile, StringComparison.Ordinal)).ToList();
        }

        private static IEnumerable<RemoteAsset> ReleaseAssets(object release, string repo)
        {
            var version = Json.Str(release, "tag_name").TrimStart('v', 'V');
            var tag = Json.Str(release, "tag_name");
            var id = Json.Str(release, "id");
            DateTimeOffset published;
            DateTimeOffset.TryParse(Json.Str(release, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out published);
            foreach (var item in Json.List(Json.Get(release, "assets")))
            {
                var asset = RemoteAsset.From(item, repo, version);
                if (asset == null) continue;
                asset.Origin.Tag = tag;
                asset.Origin.ReleaseId = id;
                asset.Origin.Published = published == default(DateTimeOffset) ? 0 : published.UtcDateTime.Ticks;
                yield return asset;
            }
        }

        internal async Task<RemoteAsset> SmapiRelease()
        {
            var release = await Request("https://api.github.com/repos/" + SmapiRepo + "/releases/latest").ConfigureAwait(false);
            var installer = ReleaseAssets(release, SmapiRepo).FirstOrDefault(file =>
                Regex.IsMatch(file.Version, @"^\d+\.\d+\.\d+(\.\d+)?$") &&
                string.Equals(file.Name, "SMAPI-" + file.Version + "-installer.zip", StringComparison.OrdinalIgnoreCase));
            if (installer == null) throw new IOException("В последнем релизе SMAPI не найден официальный установщик для Windows.");
            var minimum = Regex.Match(PlainText(Json.Str(release, "body")), @"(?:requires|for)\s+Stardew Valley\s+(\d+\.\d+(?:\.\d+)?)",
                RegexOptions.IgnoreCase);
            installer.MinimumGame = minimum.Success ? minimum.Groups[1].Value : "1.6.14";
            return installer;
        }
    }

    internal sealed partial class Store
    {
        internal string InstalledSmapi()
        {
            return !string.IsNullOrEmpty(Settings.Game) && File.Exists(Path.Combine(Settings.Game, "StardewModdingAPI.exe"))
                ? GameVersion("StardewModdingAPI.dll") : "";
        }

        internal void InstallSmapi(string archive, string expected, string minimum, Action<string> status)
        {
            string game = Path.GetFullPath(Settings.Game);
            if (!IsGame(game)) throw new IOException("Папка Stardew Valley не найдена.");
            string gameVersion = GameVersion("Stardew Valley.dll");
            if (gameVersion.Length == 0) throw new IOException("Не удалось определить версию игры. Проверь её файлы.");
            if (CompareVersion(gameVersion, minimum) < 0)
                throw new IOException("Для SMAPI " + expected + " нужна Stardew Valley " + minimum + " или новее. Сейчас установлена " + gameVersion + ".");
            string work = Path.Combine(game, ".lug-smapi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            bool preserve = false;
            try
            {
                status("Распаковываю SMAPI…");
                string bundle = Path.Combine(work, "bundle");
                ExtractWindowsPayload(archive, work, bundle);
                var dllPath = Path.Combine(bundle, "StardewModdingAPI.dll");
                if (!File.Exists(dllPath) || !File.Exists(Path.Combine(bundle, "StardewModdingAPI.exe")) ||
                    !Directory.Exists(Path.Combine(bundle, "smapi-internal")) ||
                    CompareVersion(System.Diagnostics.FileVersionInfo.GetVersionInfo(dllPath).FileVersion, expected) != 0)
                    throw new IOException("Файлы SMAPI не соответствуют версии " + expected + ".");
                Copy(Path.Combine(game, "Stardew Valley.deps.json"), Path.Combine(bundle, "StardewModdingAPI.deps.json"));
                string userConfig = Path.Combine("smapi-internal", "config.user.json");
                string oldConfig = Path.Combine(game, userConfig);
                if (File.Exists(oldConfig) && !IsLink(oldConfig) && !IsLink(Path.GetDirectoryName(oldConfig)))
                    Copy(oldConfig, Path.Combine(bundle, userConfig));
                var files = Directory.EnumerateFiles(bundle).Select(Path.GetFileName)
                    .Where(name => Regex.IsMatch(name, @"^StardewModdingAPI\.(dll|exe|exe.config|runtimeconfig.json|deps.json|xml|pdb)$", RegexOptions.IgnoreCase))
                    .ToList();
                files.Add("smapi-internal");
                if (!File.Exists(Path.Combine(game, "steam_appid.txt")) && File.Exists(Path.Combine(bundle, "steam_appid.txt")))
                    files.Add("steam_appid.txt");
                var backed = new List<string>();
                var placed = new List<string>();
                string backup = Path.Combine(work, "previous");
                Directory.CreateDirectory(backup);
                status("Устанавливаю SMAPI " + expected + "…");
                try
                {
                    foreach (var name in files)
                    {
                        string target = Path.Combine(game, name);
                        if (File.Exists(target) || Directory.Exists(target))
                        {
                            if (IsLink(target)) throw new IOException("Файл SMAPI является ссылкой: " + name);
                            MovePath(target, Path.Combine(backup, name));
                            backed.Add(name);
                        }
                        MovePath(Path.Combine(bundle, name), target);
                        placed.Add(name);
                    }
                    if (InstalledSmapi().Length == 0 || CompareVersion(InstalledSmapi(), expected) != 0)
                        throw new IOException("Не удалось подтвердить установленную версию SMAPI.");
                }
                catch
                {
                    preserve = true;
                    foreach (var name in placed.AsEnumerable().Reverse())
                        MovePath(Path.Combine(game, name), Path.Combine(bundle, name));
                    foreach (var name in backed.AsEnumerable().Reverse())
                        MovePath(Path.Combine(backup, name), Path.Combine(game, name));
                    preserve = false;
                    throw;
                }
            }
            finally
            {
                if (!preserve)
                    try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static void MovePath(string source, string target)
        {
            if (Directory.Exists(source)) Directory.Move(source, target);
            else File.Move(source, target);
        }

        private static void ExtractWindowsPayload(string archive, string work, string bundle)
        {
            string data = Path.Combine(work, "install.dat");
            using (var zip = System.IO.Compression.ZipFile.OpenRead(archive))
            {
                var files = zip.Entries.Where(file => file.FullName.Replace('\\', '/').EndsWith("/internal/windows/install.dat",
                    StringComparison.OrdinalIgnoreCase) || Names.Equals(file.FullName.Replace('\\', '/'), "internal/windows/install.dat")).ToList();
                if (files.Count != 1 || files[0].Length > 64L * 1024 * 1024) throw new IOException("В релизе нет корректного пакета SMAPI для Windows.");
                using (var input = files[0].Open())
                using (var output = File.Create(data)) input.CopyTo(output);
            }
            Directory.CreateDirectory(bundle);
            ExtractZip(data, bundle);
        }
    }
}
