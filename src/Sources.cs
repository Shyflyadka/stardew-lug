using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace Lug
{
    internal sealed class CatalogMod
    {
        internal int Id;
        internal string Name, Author, Summary, Version;
    }

    internal sealed class ReleaseFile
    {
        internal int Id;
        internal string Name, Version, Category, AssetUrl, Size;
        internal bool Recommended;
        public override string ToString()
        {
            return (Recommended ? "◆  " : "") + Name + (Version.Length > 0 ? "  ·  " + Version : "") +
                (Category.Length > 0 ? "  ·  " + Category : "") + (Size.Length > 0 ? "  ·  " + Size : "");
        }
    }

    internal sealed class RelatedMod
    {
        internal int Id;
        internal string Name;
        public override string ToString() { return Name; }
    }

    internal sealed class NxmLink
    {
        internal int ModId, FileId;
        internal string Key, Expires, UserId;

        internal static NxmLink Parse(string text)
        {
            Uri link;
            if (!Uri.TryCreate(text, UriKind.Absolute, out link) ||
                !link.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase) ||
                !link.Host.Equals("stardewvalley", StringComparison.OrdinalIgnoreCase) ||
                link.UserInfo.Length > 0 || !link.IsDefaultPort) return null;
            var match = Regex.Match(link.AbsolutePath, @"^/mods/([0-9]+)/files/([0-9]+)/?$", RegexOptions.IgnoreCase);
            int mod, file;
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out mod) ||
                !int.TryParse(match.Groups[2].Value, out file) || mod < 1 || file < 1) return null;
            var query = HttpUtility.ParseQueryString(link.Query);
            return new NxmLink { ModId = mod, FileId = file, Key = query["key"], Expires = query["expires"], UserId = query["user_id"] };
        }
    }

    internal sealed partial class Sources
    {
        private const string Api = "https://api.nexusmods.com";
        private const string Game = "/v1/games/stardewvalley/mods/";
        internal const string NexusAccount = "https://www.nexusmods.com/users/myaccount?tab=api";
        private readonly HttpClient http;
        private readonly HttpClient nexus;
        private readonly HttpClient publisher;
        private readonly Dictionary<int, Tuple<DateTime, object>> fileCache = new Dictionary<int, Tuple<DateTime, object>>();
        private readonly ConcurrentDictionary<int, string> descriptions = new ConcurrentDictionary<int, string>();

        internal Sources() : this(
            new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate },
            new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate, AllowAutoRedirect = false }) { }

        internal Sources(HttpMessageHandler publicHandler, HttpMessageHandler nexusHandler, HttpMessageHandler publishHandler = null)
        {
            http = new HttpClient(publicHandler) { Timeout = TimeSpan.FromSeconds(40) };
            nexus = new HttpClient(nexusHandler) { Timeout = TimeSpan.FromSeconds(30) };
            publisher = new HttpClient(publishHandler ?? new HttpClientHandler { AllowAutoRedirect = false })
                { Timeout = Timeout.InfiniteTimeSpan };
            foreach (var client in new[] { http, nexus, publisher })
                client.DefaultRequestHeaders.UserAgent.ParseAdd("StardewLug/1.5");
        }

        internal static string Page(int id) { return "https://www.nexusmods.com/stardewvalley/mods/" + id; }
        internal static string FilePage(int id, int file) { return Page(id) + "?tab=files&file_id=" + file; }

        internal static int NexusId(string input)
        {
            int id;
            if (int.TryParse(input, out id)) return Math.Max(0, id);
            Uri uri;
            if (Uri.TryCreate(input, UriKind.Absolute, out uri) &&
                (uri.Host.Equals("www.nexusmods.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("nexusmods.com", StringComparison.OrdinalIgnoreCase)))
            {
                var hit = Regex.Match(uri.AbsolutePath, @"^/stardewvalley/mods/(\d+)(?:/|$)", RegexOptions.IgnoreCase);
                if (hit.Success && int.TryParse(hit.Groups[1].Value, out id)) return id;
            }
            return 0;
        }

        internal static string GitRepo(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            Uri uri;
            if (Uri.TryCreate(path, UriKind.Absolute, out uri))
            {
                if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return null;
                path = string.Join("/", uri.AbsolutePath.Trim('/').Split('/').Take(2));
            }
            return Regex.IsMatch(path, @"^[a-zA-Z0-9-]+/[a-zA-Z0-9_.-]+$") ? path : null;
        }

        private async Task<object> Request(string url, object body = null, string key = "")
        {
            var uri = new Uri(url);
            bool isNexus = uri.Scheme == "https" && uri.Host == "api.nexusmods.com";
            using (var request = new HttpRequestMessage(body == null ? HttpMethod.Get : HttpMethod.Post, uri))
            {
                request.Headers.Accept.ParseAdd("application/json");
                if (isNexus)
                {
                    request.Headers.TryAddWithoutValidation("application-name", "Stardew Lug");
                    request.Headers.TryAddWithoutValidation("application-version", "1.5");
                    if (key.Length > 0) request.Headers.TryAddWithoutValidation("apikey", key);
                }
                if (body != null)
                    request.Content = new StringContent(Json.Serializer().Serialize(body), Encoding.UTF8, "application/json");
                HttpResponseMessage response;
                try { response = await (isNexus ? nexus : http).SendAsync(request).ConfigureAwait(false); }
                catch (HttpRequestException) { throw new IOException("Не удалось связаться с " + uri.Host + ". Проверь подключение и повтори."); }
                catch (TaskCanceledException) { throw new IOException("Сайт " + uri.Host + " слишком долго отвечает."); }
                using (response)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new IOException("Nexus отклонил ключ. Проверь подключение в настройках.");
                    if ((int)response.StatusCode == 429)
                        throw new IOException("Сайт временно ограничил число запросов. Попробуй позже.");
                    if (!response.IsSuccessStatusCode)
                        throw new IOException(uri.Host + " вернул ошибку " + (int)response.StatusCode +
                            (isNexus && response.StatusCode == HttpStatusCode.Forbidden ? ". Проверь аккаунт Nexus или повтори скачивание через сайт." : "."));
                    string data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    try { return Json.Serializer().DeserializeObject(data); }
                    catch (ArgumentException) { throw new IOException(uri.Host + " вернул неверный ответ."); }
                    catch (InvalidOperationException) { throw new IOException(uri.Host + " вернул слишком большой или неверный ответ."); }
                }
            }
        }

        internal async Task<Tuple<string, bool>> Validate(string key)
        {
            if (key.Length < 12 || key.Any(char.IsWhiteSpace)) throw new IOException("Вставь личный API-ключ Nexus целиком.");
            var user = await Request(Api + "/v1/users/validate.json", key: key).ConfigureAwait(false);
            return Tuple.Create(Json.Str(user, "name"), string.Equals(Json.Str(user, "is_premium"), "true", StringComparison.OrdinalIgnoreCase));
        }

        internal async Task<List<CatalogMod>> Catalog(string term, int page, string key)
        {
            if (key.Length == 0) throw new IOException("Подключи Nexus в настройках, чтобы пользоваться каталогом.");
            int direct = NexusId(term);
            if (direct > 0)
            {
                var mod = await Request(Api + Game + direct + ".json", key: key).ConfigureAwait(false);
                return new List<CatalogMod> { AsCatalog(mod, "mod_id", "version") };
            }
            var filter = new Dictionary<string, object>
            {
                { "gameDomainName", new[] { new { value = "stardewvalley", op = "EQUALS" } } }
            };
            if (!string.IsNullOrWhiteSpace(term))
                filter["name"] = new[] { new { value = "*" + term.Replace("*", "").Replace("?", "") + "*", op = "WILDCARD" } };
            var data = await Request(Api + "/v2/graphql", new
            {
                query = "query($filter: ModsFilter!, $offset: Int!) { mods(filter: $filter, count: 30, offset: $offset, sort: { endorsements: { direction: DESC } }) { nodes { modId name author summary version } } }",
                variables = new { filter = filter, offset = 30 * page }
            }, key).ConfigureAwait(false);
            if (Json.List(Json.Get(data, "errors")).Any())
            {
                // The search API may reject unavailable fields or sorting as its schema evolves.
                data = await Request(Api + "/v2/graphql", new
                {
                    query = "query($filter: ModsFilter!, $offset: Int!) { mods(filter: $filter, count: 30, offset: $offset) { nodes { modId name } } }",
                    variables = new { filter = filter, offset = 30 * page }
                }, key).ConfigureAwait(false);
                if (Json.List(Json.Get(data, "errors")).Any()) throw new IOException("Nexus временно не отдаёт каталог. Можно открыть мод по ссылке или номеру.");
            }
            return Json.List(Json.Get(Json.Get(Json.Get(data, "data"), "mods"), "nodes"))
                .Select(item => AsCatalog(item, "modId", "version")).Where(mod => mod.Id > 0).ToList();
        }

        private static CatalogMod AsCatalog(object mod, string idKey, string versionKey)
        {
            return new CatalogMod
            {
                Id = Json.Num(mod, idKey), Name = Json.Str(mod, "name"), Author = Json.Str(mod, "author"),
                Summary = PlainText(Json.Str(mod, "summary")), Version = Json.Str(mod, versionKey)
            };
        }

        internal static string PlainText(string html)
        {
            string separated = Regex.Replace(html ?? "", @"</?(?:br|p|li|div)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
            string text = HttpUtility.HtmlDecode(Regex.Replace(separated, @"<[^>]*>", ""));
            return Regex.Replace(text, @"(?:\r?\n[ \t]*){3,}", "\n\n").Trim();
        }

        internal bool CachedDescription(int id, out string description) { return descriptions.TryGetValue(id, out description); }

        internal async Task<string> NexusDescription(int id, string key)
        {
            string cached;
            if (descriptions.TryGetValue(id, out cached)) return cached;
            var data = await Request(Api + Game + id + ".json", key: key).ConfigureAwait(false);
            string summary = PlainText(Json.Str(data, "summary"));
            string full = PlainText(Json.Str(data, "description"));
            string result = full.Length == 0 ? summary : full;
            if (result.Length > 24000) result = result.Substring(0, 24000) + "…";
            descriptions[id] = result;
            return result;
        }

        internal async Task<Tuple<List<RelatedMod>, bool>> NexusRelated(int id, int page, string key)
        {
            const int pageSize = 30;
            var data = await Request(Api + "/v2/graphql", new
            {
                query = "query($ids: [CompositeDomainWithIdInput!]!, $offset: Int!) {" +
                    " legacyModsByDomain(ids: $ids, count: 1) { nodes { gameId modRequirements {" +
                    " modsRequiringThisMod(count: 30, offset: $offset) { totalCount nodes { modId modName gameId url } } } } } }",
                variables = new { ids = new[] { new { gameDomain = "stardewvalley", modId = id } }, offset = page * pageSize }
            }, key).ConfigureAwait(false);
            if (Json.List(Json.Get(data, "errors")).Any())
                throw new IOException("Nexus сейчас не отдаёт список связанных модов. Его можно посмотреть в Requirements на сайте.");
            var node = Json.List(Json.Get(Json.Get(Json.Get(data, "data"), "legacyModsByDomain"), "nodes")).FirstOrDefault();
            if (node == null) throw new IOException("Nexus не нашёл страницу этого мода.");
            var relatives = Json.Get(Json.Get(node, "modRequirements"), "modsRequiringThisMod");
            int gameId = Json.Num(node, "gameId");
            int rows = Json.Num(relatives, "totalCount");
            var results = Json.List(Json.Get(relatives, "nodes"))
                .Where(row => (gameId > 0 && Json.Num(row, "gameId") == gameId) || NexusId(Json.Str(row, "url")) > 0)
                .Select(row => new RelatedMod { Id = Json.Num(row, "modId"), Name = Json.Str(row, "modName") })
                .Where(item => item.Id > 0 && item.Id != id && !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Id).Select(group => group.First()).ToList();
            return Tuple.Create(results, (page + 1) * pageSize < rows);
        }

        internal async Task<List<ReleaseFile>> NexusFiles(int modId, string key, Mod installed)
        {
            if (key.Length == 0) throw new IOException("Подключи Nexus в настройках.");
            Tuple<DateTime, object> cache;
            object data;
            if (fileCache.TryGetValue(modId, out cache) && DateTime.UtcNow - cache.Item1 < TimeSpan.FromMinutes(2)) data = cache.Item2;
            else
            {
                data = await Request(Api + Game + modId + "/files.json", key: key).ConfigureAwait(false);
                fileCache[modId] = Tuple.Create(DateTime.UtcNow, data);
            }
            var updates = Json.List(Json.Get(data, "file_updates")).ToList();
            int target = installed == null ? 0 : installed.Meta.FileId;
            var seen = new HashSet<int>();
            while (target > 0 && seen.Add(target))
            {
                var next = updates.Where(x => Json.Num(x, "old_file_id") == target).Select(x => Json.Num(x, "new_file_id")).Distinct().ToList();
                if (next.Count != 1 || next[0] == 0 || seen.Contains(next[0])) break;
                target = next[0];
            }
            var files = Json.List(Json.Get(data, "files")).Select(row =>
            {
                string category = Json.Str(row, "category_name").ToUpperInvariant();
                string ver = Json.Str(row, "version");
                int fileId = Json.Num(row, "file_id");
                int kb = Json.Num(row, "size_kb");
                if (kb == 0) kb = Json.Num(row, "size");
                return new ReleaseFile
                {
                    Id = fileId, Name = Json.Str(row, "name"), Version = ver, Category = category,
                    Size = kb > 0 ? (kb / 1024d).ToString("0.0") + " МБ" : "",
                    Recommended = installed != null && ((target > 0 && target != installed.Meta.FileId && target == fileId) ||
                        (category == "MAIN" && ver == installed.NewVersion && ver.Length > 0))
                };
            }).Where(file => file.Id > 0 && file.Category != "ARCHIVED" && file.Category != "OLD_VERSION" && file.Category != "DELETED")
                .OrderByDescending(file => file.Recommended).ThenBy(file => file.Category != "MAIN").ThenByDescending(file => file.Id).ToList();
            return files;
        }

        internal async Task<List<ReleaseFile>> GitFiles(string repo, string preferredAsset, string version = null)
        {
            if (!string.Equals(repo, GitRepo(repo), StringComparison.Ordinal)) throw new IOException("Некорректная ссылка GitHub.");
            var data = await Request("https://api.github.com/repos/" + repo + "/releases/latest").ConfigureAwait(false);
            string release = Json.Str(data, "tag_name").TrimStart('v', 'V');
            if (!string.IsNullOrEmpty(version) && !Store.SameVersion(release, version))
                throw new IOException("GitHub показывает выпуск " + release + ", а SMAPI рекомендует " + version + ". Выбери выпуск на странице проекта.");
            return Json.List(Json.Get(data, "assets")).Select(item => new ReleaseFile
            {
                Id = Json.Num(item, "id"), Name = Json.Str(item, "name"), Version = release, Category = "",
                AssetUrl = Json.Str(item, "browser_download_url"), Size = (Json.Num(item, "size") / 1048576d).ToString("0.0") + " МБ",
                Recommended = Json.Str(item, "name") == preferredAsset
            }).Where(file => Regex.IsMatch(file.Name, @"\.(zip|7z|rar)$", RegexOptions.IgnoreCase)
                && file.AssetUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)).OrderByDescending(file => file.Recommended).ToList();
        }

        internal async Task<int> CheckUpdates(Store store, List<Mod> mods, Action<string> status)
        {
            string smapi = store.GameVersion("StardewModdingAPI.dll");
            string game = store.GameVersion("Stardew Valley.dll");
            if (smapi.Length == 0) throw new IOException("Нужен SMAPI. Проверь путь к игре в настройках.");
            var found = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var candidates = mods.GroupBy(mod => mod.Id + "\n" + mod.Version + "\n" + mod.NexusId + "\n" + string.Join(",", mod.Keys), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.ToList()).ToList();
            for (int offset = 0; offset < candidates.Count; offset += 75)
            {
                status("Проверяю данные SMAPI: " + Math.Min(offset + 75, candidates.Count) + " / " + candidates.Count);
                var batch = candidates.Skip(offset).Take(75).Select((group, i) =>
                {
                    var mod = group[0];
                    var keys = mod.Keys.AsEnumerable();
                    if (mod.Meta.NexusId > 0)
                        keys = keys.Where(k => !k.StartsWith("Nexus:", StringComparison.OrdinalIgnoreCase)).Concat(new[] { "Nexus:" + mod.Meta.NexusId });
                    return new { id = mod.Id, updateKeys = keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), installedVersion = mod.Version, index = offset + i };
                }).ToList();
                // SMAPI indexes each request by manifest ID. Separate copies with the same ID into their own requests.
                var waves = batch.GroupBy(row => row.id, StringComparer.OrdinalIgnoreCase).Select(g => g.ToArray()).ToList();
                for (int wave = 0; wave < waves.Max(g => g.Length); wave++)
                {
                    var query = waves.Where(g => g.Length > wave).Select(g => g[wave]).ToList();
                    var data = await Request("https://smapi.io/api/v4.0.0/mods", new
                    {
                        mods = query.Select(row => new { row.id, row.updateKeys, row.installedVersion }).ToArray(),
                        apiVersion = smapi, gameVersion = game, platform = "Windows", includeExtendedMetadata = true
                    }).ConfigureAwait(false);
                    foreach (var row in Json.List(data))
                    {
                        var match = query.FirstOrDefault(q => string.Equals(q.id, Json.Str(row, "id"), StringComparison.OrdinalIgnoreCase));
                        if (match == null) continue;
                        found[match.index.ToString()] = row;
                    }
                }
            }
            int count = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                object response;
                found.TryGetValue(i.ToString(), out response);
                var update = Json.Get(response, "suggestedUpdate");
                var metadata = Json.Get(response, "metadata");
                foreach (var mod in candidates[i])
                {
                    var version = Json.Str(update, "version");
                    var url = Json.Str(update, "url");
                    bool wrongNexus = mod.Meta.NexusId > 0 && NexusId(url) > 0 && mod.Meta.NexusId != NexusId(url);
                    mod.NewVersion = wrongNexus ? "" : version;
                    mod.UpdateUrl = wrongNexus ? "" : url;
                    mod.OnlineStatus = Json.Str(metadata, "compatibilityStatus");
                    mod.OnlineNote = PlainText(Json.Str(metadata, "compatibilitySummary"));
                    mod.BrokeIn = Json.Str(metadata, "brokeIn");
                    if (mod.NewVersion.Length > 0) count++;
                }
            }
            return count;
        }

        internal async Task<Uri> NexusDownload(NxmLink item, string key)
        {
            if (key.Length == 0) throw new IOException("Подключи аккаунт Nexus в настройках и повтори скачивание.");
            string query = string.IsNullOrWhiteSpace(item.Key) ? "" : "?key=" + Uri.EscapeDataString(item.Key) +
                "&expires=" + Uri.EscapeDataString(item.Expires ?? "") + "&user_id=" + Uri.EscapeDataString(item.UserId ?? "");
            var data = await Request(Api + Game + item.ModId + "/files/" + item.FileId + "/download_link.json" + query, key: key).ConfigureAwait(false);
            foreach (var mirror in Json.List(data))
            {
                Uri address;
                if (Uri.TryCreate(Json.Str(mirror, "URI"), UriKind.Absolute, out address) && address.Scheme == "https")
                    return address;
            }
            throw new IOException("У файла нет доступного адреса скачивания.");
        }

        internal async Task Download(Uri uri, string destination, Action<string> status, long limit = 4L * 1024 * 1024 * 1024)
        {
            if (uri.Scheme != "https") throw new IOException("Источник скачивания должен использовать HTTPS.");
            using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(90)))
            using (var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode || response.RequestMessage.RequestUri.Scheme != "https")
                    throw new IOException("Скачивание не удалось. Код ответа " + (int)response.StatusCode + ".");
                long size = response.Content.Headers.ContentLength ?? 0;
                if (size > limit) throw new IOException("Файл превышает допустимый размер для этой загрузки.");
                long read = 0;
                status("Скачиваю…");
                using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, useAsync: true))
                {
                    var data = new byte[131072];
                    int n;
                    var last = System.Diagnostics.Stopwatch.StartNew();
                    while ((n = await source.ReadAsync(data, 0, data.Length, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        read += n;
                        if (read > limit) throw new IOException("Файл превышает допустимый размер для этой загрузки.");
                        await target.WriteAsync(data, 0, n, timeout.Token).ConfigureAwait(false);
                        if (last.ElapsedMilliseconds < 200) continue;
                        status(size > 0 ? "Скачиваю… " + Math.Min(100, 100 * read / size) + "%" : "Скачиваю… " + (read / 1048576) + " МБ");
                        last.Restart();
                    }
                }
                if (size > 0 && size != read) throw new IOException("Скачивание прервалось. Повтори попытку.");
            }
        }
    }
}
