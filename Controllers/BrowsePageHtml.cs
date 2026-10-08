using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    internal static class BrowsePageHtml
    {
        public const string PagePath = "/media_ccc/browse";

        public static IReadOnlyList<ConferenceDto> FilterConferences(
            IEnumerable<ConferenceDto> conferences,
            string? search,
            string? year)
        {
            var term = search?.Trim();
            return conferences.Where(conference =>
                    (string.IsNullOrEmpty(term)
                     || (conference.Title ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)
                     || (conference.Acronym ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrEmpty(year)
                        || (conference.UpdatedAt.HasValue
                            && conference.UpdatedAt.Value.Year.ToString(CultureInfo.InvariantCulture) == year)))
                .ToArray();
        }

        public static string BuildUrl(string? language, string? query, string? year, string? conference, string? status = null)
        {
            var parameters = new List<string>();
            AddParameter(parameters, "lang", language);
            AddParameter(parameters, "q", query);
            AddParameter(parameters, "year", year);
            AddParameter(parameters, "conference", conference);
            AddParameter(parameters, "status", status);
            return PagePath + (parameters.Count == 0 ? string.Empty : "?" + string.Join("&", parameters));
        }

        public static string Render(
            string? userName,
            IReadOnlyList<ConferenceDto>? conferences,
            string? search,
            string? year,
            ConferenceDto? selectedConference,
            IReadOnlyList<EventDto>? events,
            bool eventError,
            bool? queued,
            bool retry,
            string language,
            Translations translations,
            string? error = null,
            string? identifyConference = null)
        {
            var html = new StringBuilder();
            HtmlPage.Begin(html, language, translations["browse.title"]);
            HtmlPage.RenderLanguageSwitcher(html, PagePath, language, translations);
            html.Append("<main class=\"wrap\"><h1>").Append(HtmlPage.Escape(translations["browse.title"])).Append("</h1>");

            if (userName is null)
            {
                RenderIdentifyForm(html, search, year, selectedConference?.Acronym ?? identifyConference, language, translations, error);
                html.Append("</main>");
                HtmlPage.End(html);
                return html.ToString();
            }

            html.Append("<p class=\"who\">").Append(HtmlPage.Escape(userName)).Append("</p>");
            if (error is not null)
            {
                html.Append("<p class=\"error\">").Append(HtmlPage.Escape(error)).Append("</p>");
            }
            if (queued == true)
            {
                html.Append("<p class=\"notice\">").Append(HtmlPage.Escape(translations["browse.status.queued"])).Append("</p>");
            }
            if (retry)
            {
                html.Append("<p class=\"error\">").Append(HtmlPage.Escape(translations["browse.button.retryDownload"])).Append("</p>");
            }

            if (conferences is null)
            {
                html.Append("<p class=\"error\">").Append(HtmlPage.Escape(translations["browse.error.conferences"])).Append("</p>");
            }
            else
            {
                RenderConferenceFilter(html, conferences, search, year, selectedConference?.Acronym, language, translations);
                var filtered = FilterConferences(conferences, search, year);
                if (filtered.Count == 0)
                {
                    html.Append("<p class=\"muted\">").Append(HtmlPage.Escape(translations["browse.empty.conferences"])).Append("</p>");
                }
                else
                {
                    html.Append("<div class=\"grid\">");
                    foreach (var conference in filtered)
                    {
                        var title = string.IsNullOrWhiteSpace(conference.Title)
                            ? translations["browse.untitled.conference"]
                            : conference.Title;
                        var acronym = conference.Acronym ?? string.Empty;
                        html.Append("<article class=\"card\"><form method=\"post\" action=\"")
                            .Append(PagePath).Append("/events?lang=").Append(Uri.EscapeDataString(language))
                            .Append("\"><img src=\"https://api.media.ccc.de/public/conferences/")
                            .Append(Uri.EscapeDataString(acronym)).Append("/poster\" alt=\"")
                            .Append(HtmlPage.Escape(title)).Append("\" /><h2>")
                            .Append(HtmlPage.Escape(title)).Append("</h2><p class=\"muted\">")
                            .Append(HtmlPage.Escape(acronym)).Append("</p>");
                        Hidden(html, "lang", language);
                        Hidden(html, "q", search);
                        Hidden(html, "year", year);
                        Hidden(html, "conference", acronym);
                        html.Append("<button type=\"submit\">").Append(HtmlPage.Escape(translations["browse.label.conference"]))
                            .Append("</button></form></article>");
                    }
                    html.Append("</div>");
                }
            }

            if (selectedConference is not null)
            {
                RenderEvents(html, selectedConference, events, eventError, search, year, language, translations);
            }

            html.Append("</main>");
            HtmlPage.End(html);
            return html.ToString();
        }

        private static void RenderIdentifyForm(
            StringBuilder html,
            string? search,
            string? year,
            string? conference,
            string language,
            Translations translations,
            string? error)
        {
            html.Append("<form method=\"post\" action=\"").Append(PagePath).Append("/identify?lang=")
                .Append(Uri.EscapeDataString(language)).Append("\"><fieldset><legend>")
                .Append(HtmlPage.Escape(translations["userSettings.legend.identify"])).Append("</legend><p class=\"hint\">")
                .Append(HtmlPage.Escape(translations["userSettings.hint.identify"])).Append("</p>");
            if (error is not null)
            {
                html.Append("<p class=\"error\">").Append(HtmlPage.Escape(error)).Append("</p>");
            }
            Hidden(html, "lang", language);
            Hidden(html, "q", search);
            Hidden(html, "year", year);
            Hidden(html, "conference", conference);
            html.Append("<label for=\"apikey\">").Append(HtmlPage.Escape(translations["userSettings.label.apiKey"]))
                .Append("</label><input id=\"apikey\" name=\"apikey\" type=\"password\" autocomplete=\"off\" required />")
                .Append("<button type=\"submit\">").Append(HtmlPage.Escape(translations["userSettings.button.continue"]))
                .Append("</button></fieldset></form>");
        }

        private static void RenderConferenceFilter(
            StringBuilder html,
            IReadOnlyList<ConferenceDto> conferences,
            string? search,
            string? year,
            string? conference,
            string language,
            Translations translations)
        {
            var years = conferences.Where(item => item.UpdatedAt.HasValue)
                .Select(item => item.UpdatedAt!.Value.Year)
                .Distinct()
                .OrderByDescending(value => value)
                .ToArray();
            html.Append("<form method=\"get\" action=\"").Append(PagePath).Append("\"><fieldset><div class=\"toolbar\">")
                .Append("<label for=\"search\">").Append(HtmlPage.Escape(translations["browse.search.placeholder"]))
                .Append("</label><input id=\"search\" type=\"text\" name=\"q\" placeholder=\"")
                .Append(HtmlPage.Escape(translations["browse.search.placeholder"])).Append("\" value=\"")
                .Append(HtmlPage.Escape(search)).Append("\" /><label for=\"year\">")
                .Append(HtmlPage.Escape(translations["browse.filter.year"])).Append("</label><select id=\"year\" name=\"year\"><option value=\"\"");
            if (string.IsNullOrEmpty(year))
            {
                html.Append(" selected");
            }
            html.Append('>').Append(HtmlPage.Escape(translations["browse.filter.allYears"])).Append("</option>");
            foreach (var item in years)
            {
                var value = item.ToString(CultureInfo.InvariantCulture);
                html.Append("<option value=\"").Append(value).Append('"');
                if (string.Equals(year, value, StringComparison.Ordinal))
                {
                    html.Append(" selected");
                }
                html.Append('>').Append(value).Append("</option>");
            }
            Hidden(html, "lang", language);
            Hidden(html, "conference", conference);
            html.Append("</select><button type=\"submit\">").Append(HtmlPage.Escape(translations["browse.filter.year"]))
                .Append("</button></div></fieldset></form>");
        }

        private static void RenderEvents(
            StringBuilder html,
            ConferenceDto conference,
            IReadOnlyList<EventDto>? events,
            bool eventError,
            string? search,
            string? year,
            string language,
            Translations translations)
        {
            html.Append("<section><h2>").Append(HtmlPage.Escape(translations["browse.label.conferenceEvents"]))
                .Append(": ").Append(HtmlPage.Escape(string.IsNullOrWhiteSpace(conference.Title)
                    ? translations["browse.untitled.conference"]
                    : conference.Title)).Append("</h2>");
            if (eventError)
            {
                html.Append("<p class=\"error\">").Append(HtmlPage.Escape(translations["browse.error.events"])).Append("</p>");
            }
            else if (events is null || events.Count == 0)
            {
                html.Append("<p class=\"muted\">").Append(HtmlPage.Escape(translations["browse.empty.events"])).Append("</p>");
            }
            else
            {
                html.Append("<div class=\"grid\">");
                foreach (var item in events)
                {
                    html.Append("<article class=\"card\"><h3>").Append(HtmlPage.Escape(string.IsNullOrWhiteSpace(item.Title)
                        ? translations["browse.untitled.event"]
                        : item.Title)).Append("</h3><form method=\"post\" action=\"")
                        .Append(PagePath).Append("/add?lang=").Append(Uri.EscapeDataString(language)).Append("\">");
                    Hidden(html, "lang", language);
                    Hidden(html, "q", search);
                    Hidden(html, "year", year);
                    Hidden(html, "conference", conference.Acronym);
                    Hidden(html, "eventGuid", item.Guid);
                    html.Append("<button type=\"submit\">").Append(HtmlPage.Escape(translations["browse.button.addToWatchlist"]))
                        .Append("</button></form></article>");
                }
                html.Append("</div>");
            }
            html.Append("</section>");
        }

        private static void Hidden(StringBuilder html, string name, string? value)
        {
            html.Append("<input type=\"hidden\" name=\"").Append(name).Append("\" value=\"")
                .Append(HtmlPage.Escape(value)).Append("\" />");
        }

        private static void AddParameter(List<string> parameters, string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                parameters.Add(Uri.EscapeDataString(name) + "=" + Uri.EscapeDataString(value));
            }
        }
    }
}
