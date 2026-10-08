using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.MediaCccDe.Services;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    internal static class UserSettingsPageHtml
    {
        internal const string PagePath = "/media_ccc/settings/languages";
        private const int MaxLanguageEntries = 50;

        internal static string Render(string? userName, IReadOnlyList<string>? audioLanguages, IReadOnlyList<string>? subtitleLanguages,
            string? notice, string? error, string language, Translations translations)
        {
            var html = new StringBuilder();
            HtmlPage.Begin(html, language, translations["userSettings.title"] + " | " + translations["chrome.titleSuffix"]);
            html.Append("<div class=\"page\">");
            HtmlPage.RenderLanguageSwitcher(html, PagePath, language, translations);
            html.Append("<h1>").Append(HtmlPage.Escape(translations["userSettings.title"])).Append("</h1><p class=\"lede\">")
                .Append(HtmlPage.Escape(translations["userSettings.lede"])).Append("</p>");
            if (notice is not null) html.Append("<div class=\"notice\">").Append(HtmlPage.Escape(notice)).Append("</div>");
            if (error is not null) html.Append("<div class=\"error\">").Append(HtmlPage.Escape(error)).Append("</div>");

            if (userName is null) RenderSignIn(html, translations);
            else RenderForm(html, userName, audioLanguages ?? System.Array.Empty<string>(), subtitleLanguages ?? System.Array.Empty<string>(), translations);

            html.Append("</div>");
            HtmlPage.End(html);
            return html.ToString();
        }

        private static void RenderSignIn(StringBuilder html, Translations t)
        {
            html.Append("<fieldset><legend>").Append(HtmlPage.Escape(t["userSettings.legend.identify"])).Append("</legend><p class=\"hint\">")
                .Append(HtmlPage.Escape(t["userSettings.hint.identify"])).Append("</p><form method=\"post\" action=\"")
                .Append(PagePath).Append("/identify?lang=").Append(t.Language).Append("\"><label for=\"apikey\">").Append(HtmlPage.Escape(t["userSettings.label.apiKey"]))
                .Append("</label><input type=\"password\" id=\"apikey\" name=\"apikey\" autocomplete=\"off\" required /><button type=\"submit\">")
                .Append(HtmlPage.Escape(t["userSettings.button.continue"])).Append("</button></form></fieldset>");
        }

        private static void RenderForm(StringBuilder html, string userName, IReadOnlyList<string> audio, IReadOnlyList<string> subtitles, Translations t)
        {
            html.Append("<p class=\"who\">").Append(HtmlPage.Escape(t["userSettings.signedInAs"])).Append(" <strong>")
                .Append(HtmlPage.Escape(userName)).Append("</strong></p><form method=\"post\" action=\"").Append(PagePath).Append("?lang=").Append(t.Language).Append("\">");
            html.Append("<fieldset><legend>").Append(HtmlPage.Escape(t["userSettings.legend.languages"])).Append("</legend>");
            html.Append("<label for=\"audio\">").Append(HtmlPage.Escape(t["userSettings.label.audioLanguages"])).Append("</label>");
            html.Append("<input type=\"text\" id=\"audio\" name=\"audio\" value=\"").Append(HtmlPage.Escape(Join(audio))).Append("\" /><p class=\"hint\">")
                .Append(HtmlPage.Escape(t["userSettings.hint.audioLanguages"])).Append("</p>");
            html.Append("<label for=\"subtitles\">").Append(HtmlPage.Escape(t["userSettings.label.subtitleLanguages"])).Append("</label>");
            html.Append("<input type=\"text\" id=\"subtitles\" name=\"subtitles\" value=\"").Append(HtmlPage.Escape(Join(subtitles))).Append("\" /><p class=\"hint\">")
                .Append(HtmlPage.Escape(t["userSettings.hint.subtitleLanguages"])).Append("</p></fieldset><button type=\"submit\">")
                .Append(HtmlPage.Escape(t["userSettings.button.save"])).Append("</button></form>");
        }

        internal static List<string> SplitLanguages(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return new List<string>();
            var parts = value.Split(',', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries);
            var result = new List<string>();
            foreach (var part in parts)
            {
                if (result.Count >= MaxLanguageEntries) break;
                if (part.Length <= 16 && !result.Contains(part, System.StringComparer.OrdinalIgnoreCase)) result.Add(part);
            }
            return result;
        }

        private static string Join(IEnumerable<string> languages) => string.Join(", ", languages);
    }
}
