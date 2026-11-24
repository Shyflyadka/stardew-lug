using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lug
{
    internal static class Compatibility
    {
        private static readonly HashSet<string> ReportedWarnings = new HashSet<string>(
            new[] { "broken", "obsolete", "workaround", "unofficial", "abandoned" }, StringComparer.OrdinalIgnoreCase);

        internal static bool HasWarning(Mod mod)
        {
            if (!mod.Enabled) return false;
            return !string.IsNullOrEmpty(mod.Problem) || ReportedWarnings.Contains(mod.OnlineStatus);
        }

        internal static string OnlineLabel(Mod mod)
        {
            switch ((mod.OnlineStatus ?? "").ToLowerInvariant())
            {
                case "ok": return "Последняя версия отмечена как рабочая";
                case "broken": return "Известна несовместимость";
                case "obsolete": return "Мод устарел";
                case "workaround": return "Нужен обходной вариант";
                case "unofficial": return "Есть неофициальное обновление";
                case "abandoned": return "Поддержка прекращена";
                default: return "Статус неизвестен";
            }
        }

        internal static string LastSkipped(string log, string mods)
        {
            if (!File.Exists(log)) throw new IOException("В этой учётной записи ещё нет журнала SMAPI.");
            bool correctFolder = false;
            var issues = new List<string>();
            var headers = new Regex(@"^\[[^\]]*\bSMAPI\]\s*(.*)$");
            bool skipSection = false;
            using (var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var entry = headers.Match(line);
                    if (!entry.Success) { skipSection = false; continue; }
                    string text = entry.Groups[1].Value;
                    const string pathMarker = "Mods go here: ";
                    if (text.StartsWith(pathMarker, StringComparison.Ordinal))
                    {
                        string folder = text.Substring(pathMarker.Length).Trim();
                        if (folder.StartsWith(@"~\", StringComparison.Ordinal))
                            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder.Substring(2));
                        correctFolder = string.Equals(folder.TrimEnd('\\'), mods.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                    }
                    if (!correctFolder) continue;
                    if (text == "Skipped mods") { skipSection = true; continue; }
                    if (!skipSection || text.Length == 0 || text.All(ch => ch == '-') ||
                        text.StartsWith("These mods could not", StringComparison.Ordinal)) continue;
                    if (!text.StartsWith("- ", StringComparison.Ordinal)) { skipSection = false; continue; }
                    issues.Add(text.Substring(2));
                    if (issues.Count >= 100) break;
                }
            }
            if (!correctFolder) return "Последний журнал относится к другой папке модов. Запусти игру из Луга, чтобы увидеть результаты этой сборки.";
            return issues.Count == 0 ? "В последнем журнале SMAPI нет списка пропущенных модов." : string.Join("\r\n\r\n", issues);
        }
    }
}
