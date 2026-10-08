using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.MediaCccDe.Services;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    internal static class SettingsPageHtml
    {
        internal const string SettingsPath = "/media_ccc/settings";
        private const int MaxLanguageEntries = 50;

        internal static string Render(PluginConfiguration? configuration, string? notice, string? error, string language, Translations translations)
        {
            var html = new StringBuilder();
            HtmlPage.Begin(html, language, translations["settings.title"] + " | " + translations["chrome.titleSuffix"]);
            html.Append("<div class=\"page\">");
            HtmlPage.RenderLanguageSwitcher(html, SettingsPath, language, translations);
            html.Append("<h1>").Append(HtmlPage.Escape(translations["settings.title"])).Append("</h1>");
            html.Append("<p class=\"lede\">").Append(HtmlPage.Escape(translations["settings.lede"])).Append("</p>");

            if (notice is not null)
            {
                html.Append("<div class=\"notice\">").Append(HtmlPage.Escape(notice)).Append("</div>");
            }
            if (error is not null)
            {
                html.Append("<div class=\"error\">").Append(HtmlPage.Escape(error)).Append("</div>");
            }

            if (configuration is null)
            {
                RenderUnlock(html, translations);
            }
            else
            {
                RenderForm(html, configuration, translations);
            }

            html.Append("</div>");
            HtmlPage.End(html);
            return html.ToString();
        }

        private static void RenderUnlock(StringBuilder html, Translations t)
        {
            html.Append("<fieldset><legend>").Append(HtmlPage.Escape(t["settings.legend.administratorSignIn"])).Append("</legend>");
            html.Append("<p class=\"hint\">").Append(HtmlPage.Escape(t["settings.hint.administratorSignIn"])).Append("</p>");
            html.Append("<form method=\"post\" action=\"").Append(SettingsPath).Append("/unlock?lang=").Append(t.Language).Append("\">");
            html.Append("<label for=\"token\">").Append(HtmlPage.Escape(t["settings.label.accessToken"])).Append("</label>");
            html.Append("<input type=\"text\" id=\"token\" name=\"token\" autocomplete=\"off\" required />");
            html.Append("<button type=\"submit\">").Append(HtmlPage.Escape(t["settings.button.unlock"])).Append("</button>");
            html.Append("</form></fieldset>");
        }

        private static void RenderForm(StringBuilder html, PluginConfiguration configuration, Translations t)
        {
            html.Append("<form method=\"post\" action=\"").Append(SettingsPath).Append("?lang=").Append(t.Language).Append("\">");
            html.Append("<fieldset><legend>").Append(HtmlPage.Escape(t["settings.legend.storage"])).Append("</legend>");
            html.Append("<label for=\"WatchlistPath\">").Append(HtmlPage.Escape(t["settings.label.watchlistPath"])).Append("</label>");
            html.Append("<input type=\"text\" id=\"WatchlistPath\" name=\"WatchlistPath\" value=\"").Append(HtmlPage.Escape(configuration.WatchlistPath)).Append("\" />");
            html.Append("<p class=\"hint\">").Append(HtmlPage.Escape(t["settings.hint.watchlistPath"])).Append("</p></fieldset>");

            html.Append("<fieldset><legend>").Append(HtmlPage.Escape(t["settings.legend.synchronisation"])).Append("</legend>");
            html.Append("<label for=\"SyncIntervalHours\">").Append(HtmlPage.Escape(t["settings.label.syncInterval"])).Append("</label>");
            html.Append("<input type=\"number\" id=\"SyncIntervalHours\" name=\"SyncIntervalHours\" min=\"1\" max=\"168\" value=\"")
                .Append(configuration.SyncIntervalHours.ToString(CultureInfo.InvariantCulture)).Append("\" />");
            html.Append("<p class=\"hint\">").Append(HtmlPage.Escape(t["settings.hint.syncInterval"])).Append("</p>");
            html.Append("<label for=\"PreferredQuality\">").Append(HtmlPage.Escape(t["settings.label.preferredQuality"])).Append("</label>");
            html.Append("<select id=\"PreferredQuality\" name=\"PreferredQuality\">");
            foreach (var (value, key) in new[] { ("hd", "settings.option.qualityHd"), ("sd", "settings.option.qualitySd") })
            {
                var selected = string.Equals(configuration.PreferredQuality, value, System.StringComparison.OrdinalIgnoreCase) ? " selected" : string.Empty;
                html.Append("<option value=\"").Append(value).Append('"').Append(selected).Append('>')
                    .Append(HtmlPage.Escape(t[key])).Append("</option>");
            }
            html.Append("</select><p class=\"hint\">").Append(HtmlPage.Escape(t["settings.hint.preferredQuality"])).Append("</p></fieldset>");

            html.Append("<fieldset><legend>").Append(HtmlPage.Escape(t["settings.legend.languages"])).Append("</legend>");
            html.Append("<label for=\"PreferredAudioLanguages\">").Append(HtmlPage.Escape(t["settings.label.preferredAudioLanguages"])).Append("</label>");
            html.Append("<input type=\"text\" id=\"PreferredAudioLanguages\" name=\"PreferredAudioLanguages\" value=\"")
                .Append(HtmlPage.Escape(Join(configuration.PreferredAudioLanguages))).Append("\" /><p class=\"hint\">")
                .Append(HtmlPage.Escape(t["settings.hint.preferredAudioLanguages"])).Append("</p>");
            html.Append("<label for=\"PreferredSubtitleLanguages\">").Append(HtmlPage.Escape(t["settings.label.preferredSubtitleLanguages"])).Append("</label>");
            html.Append("<input type=\"text\" id=\"PreferredSubtitleLanguages\" name=\"PreferredSubtitleLanguages\" value=\"")
                .Append(HtmlPage.Escape(Join(configuration.PreferredSubtitleLanguages))).Append("\" /><p class=\"hint\">")
                .Append(HtmlPage.Escape(t["settings.hint.preferredSubtitleLanguages"])).Append("</p></fieldset>");

            html.Append("<fieldset><legend>").Append(HtmlPage.Escape(t["settings.legend.subtitles"])).Append("</legend><div class=\"check\">");
            html.Append("<input type=\"checkbox\" id=\"DownloadSubtitles\" name=\"DownloadSubtitles\"")
                .Append(configuration.DownloadSubtitles ? " checked" : string.Empty).Append(" /><span>")
                .Append(HtmlPage.Escape(t["settings.label.downloadSubtitles"])).Append("</span></div><p class=\"hint\">")
                .Append(HtmlPage.Escape(t["settings.hint.downloadSubtitles"])).Append("</p></fieldset>");
            html.Append("<button type=\"submit\">").Append(HtmlPage.Escape(t["common.button.save"])).Append("</button></form>");
        }

        internal static List<string> SplitLanguages(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string>();
            var parts = value.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            var result = new List<string>();
            foreach (var part in parts)
            {
                if (result.Count >= MaxLanguageEntries) break;
                if (!result.Contains(part, System.StringComparer.OrdinalIgnoreCase)) result.Add(part);
            }
            return result;
        }

        private static string Join(IEnumerable<string> languages) => string.Join(", ", languages);
    }
}
