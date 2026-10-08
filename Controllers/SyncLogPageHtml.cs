using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Jellyfin.Plugin.MediaCccDe.Services;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    internal static class SyncLogPageHtml
    {
        public const string PagePath = "/media_ccc/sync/log";

        public static string Render(IReadOnlyList<SyncLogEntry>? history, string language, Translations translations)
        {
            var html = new StringBuilder();
            HtmlPage.Begin(html, language, translations["syncLog.title"]);
            html.Append("<main class=\"wrap\">");
            HtmlPage.RenderLanguageSwitcher(html, PagePath, language, translations);
            html.Append("<h1>").Append(HtmlPage.Escape(translations["syncLog.title"])).Append("</h1>");
            html.Append("<p class=\"lede\">").Append(HtmlPage.Escape(translations["syncLog.subtitle"])).Append("</p>");
            html.Append("<div class=\"toolbar\">");
            RenderForm(html, PagePath + "/trigger", language, translations["syncLog.button.trigger"]);
            html.Append("<form method=\"get\" action=\"").Append(PagePath).Append("\">");
            RenderLanguageInput(html, language);
            html.Append("<button type=\"submit\">").Append(HtmlPage.Escape(translations["syncLog.button.refresh"])).Append("</button></form>");
            html.Append("<a href=\"/media_ccc/sync/history/confirm?lang=").Append(HtmlPage.Escape(language)).Append("\">")
                .Append(HtmlPage.Escape(translations["syncLog.button.clear"])).Append("</a></div>");

            if (history is null)
            {
                html.Append("<p class=\"muted\">").Append(HtmlPage.Escape(translations["syncLog.loading"])).Append("</p>");
            }
            else if (history.Count == 0)
            {
                html.Append("<p class=\"muted\">").Append(HtmlPage.Escape(translations["syncLog.empty"])).Append("</p>");
            }
            else
            {
                html.Append("<table class=\"list\"><caption class=\"muted\">").Append(HtmlPage.Escape(translations["syncLog.subtitle"]))
                    .Append("</caption><tbody>");
                foreach (var entry in history)
                {
                    html.Append("<tr><th scope=\"row\" class=\"status\"><span class=\"badge\">").Append(HtmlPage.Escape(entry.Status.ToString()))
                        .Append("</span></th><td>").Append(HtmlPage.Escape(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))
                        .Append("</td><td>").Append(HtmlPage.Escape(entry.ConferenceAcronym))
                        .Append("</td><td>").Append(entry.EventsProcessed.ToString(CultureInfo.InvariantCulture))
                        .Append("</td><td>").Append(entry.FilesCreated.ToString(CultureInfo.InvariantCulture))
                        .Append("</td><td>").Append(HtmlPage.Escape(entry.ErrorMessage)).Append("</td></tr>");
                }

                html.Append("</tbody></table>");
            }

            html.Append("</main>");
            HtmlPage.End(html);
            return html.ToString();
        }

        public static string RenderClearConfirmation(string language, Translations translations)
        {
            var html = new StringBuilder();
            HtmlPage.Begin(html, language, translations["syncLog.button.clear"]);
            html.Append("<main class=\"page\">");
            HtmlPage.RenderLanguageSwitcher(html, PagePath, language, translations);
            html.Append("<h1>").Append(HtmlPage.Escape(translations["syncLog.button.clear"])).Append("</h1>");
            html.Append("<p class=\"lede\">").Append(HtmlPage.Escape(translations["syncLog.confirm.clear"])).Append("</p>");
            html.Append("<form method=\"post\" action=\"/media_ccc/sync/history/confirm\">");
            RenderLanguageInput(html, language);
            html.Append("<button type=\"submit\">").Append(HtmlPage.Escape(translations["syncLog.button.clear"]))
                .Append("</button></form><p><a href=\"").Append(PagePath).Append("?lang=").Append(HtmlPage.Escape(language)).Append("\">")
                .Append(HtmlPage.Escape(translations["syncLog.button.refresh"])).Append("</a></p></main>");
            HtmlPage.End(html);
            return html.ToString();
        }

        private static void RenderForm(StringBuilder html, string action, string language, string label)
        {
            html.Append("<form method=\"post\" action=\"").Append(action).Append("\">");
            RenderLanguageInput(html, language);
            html.Append("<button type=\"submit\">").Append(HtmlPage.Escape(label)).Append("</button></form>");
        }

        private static void RenderLanguageInput(StringBuilder html, string language)
        {
            html.Append("<input type=\"hidden\" name=\"lang\" value=\"").Append(HtmlPage.Escape(language)).Append("\" />");
        }
    }
}
