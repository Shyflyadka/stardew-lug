using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Lug
{
    internal sealed partial class Sources
    {
        private const string PackAsset = "Stardew.lugpack.zip";
        private const long MaxPackUpload = 2L * 1024 * 1024 * 1024;
        private const string GitApi = "https://api.github.com/repos/";
        private const string GitUploads = "https://uploads.github.com/repos/";

        internal static bool IsPublishKey(string key)
        {
            return Regex.IsMatch(key ?? "", @"\A[A-Za-z0-9_-]{20,255}\z");
        }

        internal async Task<string> CheckPackRepository(string repo, string key)
        {
            if (repo == null || repo != GitRepo(repo) || !IsPublishKey(key))
                throw new IOException("Проверь адрес репозитория и токен GitHub.");
            var data = await PublishRequest(HttpMethod.Get, GitApi + repo, key).ConfigureAwait(false);
            string fullName = Json.Str(data, "full_name");
            if (!string.Equals(repo, fullName, StringComparison.OrdinalIgnoreCase) || !object.Equals(Json.Get(data, "private"), false) ||
                object.Equals(Json.Get(data, "archived"), true) || object.Equals(Json.Get(data, "disabled"), true))
                throw new IOException("Для публикации нужен публичный репозиторий сборки.");
            if (!Json.List(Json.Get(data, "topics")).OfType<string>().Contains(BuildTopic, StringComparer.OrdinalIgnoreCase))
                throw new IOException("Добавь тему «" + BuildTopic + "» в разделе About репозитория, чтобы сборка появилась в каталоге.");
            var last = await PublishRequest(HttpMethod.Get, GitApi + fullName + "/releases/latest", key, allowMissing: true).ConfigureAwait(false);
            if (last != null)
            {
                var assets = ReleaseAssets(last, fullName).ToList();
                if (assets.Count(item => item.Name.EndsWith(".lugpack.zip", StringComparison.OrdinalIgnoreCase)) != 1 ||
                    assets.Any(item => item.Name == LauncherUpdate.ExeName || item.Name == LauncherUpdate.PackageName))
                    throw new IOException("Для каждой сборки нужен свой репозиторий. В последнем выпуске этого репозитория уже есть другая публикация.");
            }
            return fullName;
        }

        internal async Task<string> PublishPack(string repo, string name, string path, string key, Action<string> progress)
        {
            if (repo == null || repo != GitRepo(repo) || !IsPublishKey(key))
                throw new IOException("Проверь адрес репозитория и токен GitHub.");
            var length = new FileInfo(path).Length;
            if (length == 0 || length >= MaxPackUpload) throw new IOException("Архив для публикации должен быть меньше 2 ГБ.");
            string tag = "lugpack-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string releases = GitApi + repo + "/releases";
            progress("Создаю черновик…");
            var draft = await PublishRequest(HttpMethod.Post, releases, key, body: new
            {
                tag_name = tag, name = name, body = "Сборка для Stardew Valley. Открой ссылку в разделе «Публичные сборки» в Луге.",
                draft = true, prerelease = false
            }).ConfigureAwait(false);
            string id = Json.Str(draft, "id");
            if (!Regex.IsMatch(id, @"\A[0-9]+\z") || !IsPackRelease(draft, tag, true))
                throw new IOException("GitHub не подтвердил создание черновика.");
            string edit = releases + "/" + id;
            string link = "https://github.com/" + repo + "/releases/tag/" + tag;
            IOException failure;
            try
            {
                string upload = GitUploads + repo + "/releases/" + id + "/assets?name=" + Uri.EscapeDataString(PackAsset);
                progress("Загружаю сборку…");
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, useAsync: true))
                using (var content = new PackUpload(file, progress))
                {
                    var asset = await PublishRequest(HttpMethod.Post, upload, key, content: content).ConfigureAwait(false);
                    if (Json.Str(asset, "name") != PackAsset || Json.Str(asset, "state") != "uploaded" ||
                        Json.Str(asset, "size") != length.ToString(CultureInfo.InvariantCulture))
                        throw new IOException("GitHub не подтвердил загрузку архива.");
                }
                progress("Публикую сборку…");
                var result = await PublishRequest(new HttpMethod("PATCH"), edit, key,
                    body: new { draft = false, make_latest = "true" }).ConfigureAwait(false);
                if (IsPackRelease(result, tag, false)) return link;
                failure = new IOException("GitHub не подтвердил публикацию сборки.");
            }
            catch (IOException error) { failure = error; }
            // A timed-out request may have finished on GitHub. Never delete a published release.
            try
            {
                var state = await PublishRequest(HttpMethod.Get, edit, key, allowMissing: true).ConfigureAwait(false);
                if (IsPackRelease(state, tag, false)) return link;
                if (IsPackRelease(state, tag, true))
                    await PublishRequest(HttpMethod.Delete, edit, key).ConfigureAwait(false);
            }
            catch (IOException) { }
            throw failure;
        }

        private static bool IsPackRelease(object release, string tag, bool draft)
        {
            return Json.Str(release, "tag_name") == tag && object.Equals(Json.Get(release, "draft"), draft);
        }

        private async Task<object> PublishRequest(HttpMethod method, string url, string key, object body = null,
            HttpContent content = null, bool allowMissing = false)
        {
            using (var timeout = new CancellationTokenSource(content == null ? TimeSpan.FromSeconds(30) : TimeSpan.FromMinutes(90)))
            using (var request = new HttpRequestMessage(method, url))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Content = content ?? (body == null ? null : new StringContent(Json.Serializer().Serialize(body), Encoding.UTF8, "application/json"));
                try
                {
                    using (var response = await publisher.SendAsync(request, timeout.Token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == HttpStatusCode.NoContent ||
                            (allowMissing && response.StatusCode == HttpStatusCode.NotFound)) return null;
                        if (response.StatusCode == HttpStatusCode.Unauthorized)
                            throw new IOException("GitHub отклонил токен. Обнови его в настройках публикации.");
                        if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound)
                            throw new IOException("GitHub отказал в доступе. Проверь репозиторий и право токена Contents: Read and write.");
                        if ((int)response.StatusCode == 422)
                            throw new IOException("GitHub не принял публикацию. Проверь, что в репозитории есть хотя бы один коммит.");
                        if (!response.IsSuccessStatusCode)
                            throw new IOException("GitHub не выполнил запрос (" + (int)response.StatusCode + ").");
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        try { return Json.Serializer().DeserializeObject(json); }
                        catch (ArgumentException) { throw new IOException("GitHub вернул неверный ответ."); }
                        catch (InvalidOperationException) { throw new IOException("GitHub вернул неверный ответ."); }
                    }
                }
                catch (HttpRequestException) { throw new IOException("Связь с GitHub прервалась. Попробуй ещё раз."); }
                catch (OperationCanceledException) { throw new IOException("GitHub слишком долго отвечает. Попробуй ещё раз."); }
            }
        }
    }

    internal sealed class PackUpload : HttpContent
    {
        private readonly Stream source;
        private readonly Action<string> progress;

        internal PackUpload(Stream source, Action<string> progress)
        {
            this.source = source;
            this.progress = progress;
            Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = source.Length;
            return true;
        }

        protected override async Task SerializeToStreamAsync(Stream destination, TransportContext context)
        {
            long length = source.Length, sent = 0;
            source.Position = 0;
            var buffer = new byte[131072];
            var timer = Stopwatch.StartNew();
            int count;
            while ((count = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
            {
                await destination.WriteAsync(buffer, 0, count).ConfigureAwait(false);
                sent += count;
                if (timer.ElapsedMilliseconds < 200 && sent != length) continue;
                progress("Загружаю сборку… " + (100 * sent / length) + "%");
                timer.Restart();
            }
        }
    }
}
