using System.Collections.Generic;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    internal sealed record WatchlistPageItem(string Guid, string Title, string? ConferenceAcronym);

    internal static class WatchlistPageHtml
    {
        internal const string PagePath = "/media_ccc/watchlist/page";

        internal static string Render(
            string? userName,
            IReadOnlyList<WatchlistPageItem>? events,
            IReadOnlyList<DownloadQueueItem>? queue,
            string? error,
            string language,
            Translations translations)
        {
            var html = new StringBuilder();
            HtmlPage.Begin(html, language, translations["watchlist.title"]);
            html.Append("<div class=\"wrap\">");
            HtmlPage.RenderLanguageSwitcher(html, PagePath, language, translations);
            html.Append("<h1>").Append(HtmlPage.Escape(translations["watchlist.title"]))
                .Append("</h1><p class=\"who\">").Append(HtmlPage.Escape(userName ?? string.Empty)).Append("</p>");

            if (error is not null)
            {
                html.Append("<div class=\"error\">").Append(HtmlPage.Escape(error)).Append("</div>");
            }

            if (userName is null)
            {
                RenderIdentificationForm(html, translations);
            }
            else
            {
                RenderWatchlist(html, events ?? System.Array.Empty<WatchlistPageItem>(), queue ?? System.Array.Empty<DownloadQueueItem>(), translations);
            }

            html.Append("</div>");
            HtmlPage.End(html);
            return html.ToString();
        }

        private static void RenderIdentificationForm(StringBuilder html, Translations translations)
        {
            html.Append("<fieldset><legend>").Append(HtmlPage.Escape(translations["userSettings.legend.identify"]))
                .Append("</legend><p class=\"hint\">").Append(HtmlPage.Escape(translations["userSettings.hint.identify"]))
                .Append("</p><form method=\"post\" action=\"").Append(PagePath).Append("/identify?lang=")
                .Append(HtmlPage.Escape(translations.Language)).Append("\"><label for=\"apikey\">")
                .Append(HtmlPage.Escape(translations["userSettings.label.apiKey"]))
                .Append("</label><input type=\"password\" id=\"apikey\" name=\"apikey\" autocomplete=\"off\" required />")
                .Append("<button type=\"submit\">").Append(HtmlPage.Escape(translations["userSettings.button.continue"]))
                .Append("</button></form></fieldset>");
        }

        private static void RenderWatchlist(
            StringBuilder html,
            IReadOnlyList<WatchlistPageItem> events,
            IReadOnlyList<DownloadQueueItem> queue,
            Translations translations)
        {
            var activeQueueCount = queue.Count(item => item.Status != DownloadStatus.Completed);
            html.Append("<div class=\"toolbar\"><span class=\"muted\">").Append(activeQueueCount)
                .Append(' ').Append(HtmlPage.Escape(translations["watchlist.itemsInQueue"]))
                .Append("</span><form method=\"post\" action=\"").Append(PagePath)
                .Append("/start?lang=").Append(HtmlPage.Escape(translations.Language)).Append("\"><button type=\"submit\">")
                .Append(HtmlPage.Escape(translations["watchlist.button.startDownload"]))
                .Append("</button></form></div>");

            if (events.Count == 0)
            {
                html.Append("<div class=\"card\"><h2>").Append(HtmlPage.Escape(translations["watchlist.empty.title"]))
                    .Append("</h2><p class=\"muted\">").Append(HtmlPage.Escape(translations["watchlist.empty.detail"]))
                    .Append("</p></div>");
                return;
            }

            html.Append("<table class=\"list\"><thead><tr><th>")
                .Append(HtmlPage.Escape(translations["watchlist.table.title"])).Append("</th><th>")
                .Append(HtmlPage.Escape(translations["watchlist.table.conference"])).Append("</th><th>")
                .Append(HtmlPage.Escape(translations["watchlist.table.status"])).Append("</th><th>")
                .Append(HtmlPage.Escape(translations["watchlist.table.actions"])).Append("</th></tr></thead><tbody>");

            foreach (var item in events)
            {
                var queueItem = queue.FirstOrDefault(candidate => candidate.EventGuid == item.Guid);
                var status = queueItem?.Status ?? DownloadStatus.Pending;
                var label = status switch
                {
                    DownloadStatus.Pending => translations["watchlist.status.pending"],
                    DownloadStatus.InProgress => translations["watchlist.status.downloading"],
                    DownloadStatus.Completed => translations["watchlist.status.ready"],
                    DownloadStatus.Failed => translations["watchlist.status.failed"],
                    _ => translations["watchlist.status.pending"]
                };

                html.Append("<tr><td>").Append(HtmlPage.Escape(item.Title)).Append("</td><td>")
                    .Append(HtmlPage.Escape(string.IsNullOrWhiteSpace(item.ConferenceAcronym) ? translations["common.unknown"] : item.ConferenceAcronym))
                    .Append("</td><td><span class=\"status\"><span class=\"badge\">").Append(HtmlPage.Escape(label))
                    .Append("</span></span>");
                if (status == DownloadStatus.InProgress && queueItem is not null)
                {
                    var percent = (int)System.Math.Round(queueItem.Progress * 100, MidpointRounding.AwayFromZero);
                    html.Append("<p class=\"muted\">").Append(percent).Append(HtmlPage.Escape(translations["watchlist.downloadedPercent"]))
                        .Append("</p>");
                }

                html.Append("</td><td>");
                if (status == DownloadStatus.Failed)
                {
                    RenderActionForm(html, "retry", item.Guid, translations["watchlist.button.retry"], translations.Language);
                }

                html.Append("<form method=\"post\" action=\"").Append(PagePath).Append("/remove?lang=")
                    .Append(HtmlPage.Escape(translations.Language)).Append("\"><input type=\"hidden\" name=\"eventGuid\" value=\"")
                    .Append(HtmlPage.Escape(item.Guid)).Append("\" /><span class=\"hint\">")
                    .Append(HtmlPage.Escape(translations["watchlist.confirm.remove"]))
                    .Append("</span><button type=\"submit\">").Append(HtmlPage.Escape(translations["watchlist.button.remove"]))
                    .Append("</button></form></td></tr>");
            }

            html.Append("</tbody></table>");
        }

        private static void RenderActionForm(StringBuilder html, string action, string eventGuid, string label, string language)
        {
            html.Append("<form method=\"post\" action=\"").Append(PagePath).Append('/').Append(action).Append("?lang=")
                .Append(HtmlPage.Escape(language)).Append("\"><input type=\"hidden\" name=\"eventGuid\" value=\"")
                .Append(HtmlPage.Escape(eventGuid)).Append("\" /><button type=\"submit\">")
                .Append(HtmlPage.Escape(label)).Append("</button></form>");
        }
    }
}
