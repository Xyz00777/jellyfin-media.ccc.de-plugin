using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class BrowsePageTests
    {
        [Fact]
        public void English_page_uses_english_catalog()
        {
            var html = Render("en");

            Assert.Contains("<html lang=\"en\">", html);
            Assert.Contains("Browse Conferences", html);
            Assert.Contains("Search conferences...", html);
        }

        [Fact]
        public void German_page_uses_german_catalog()
        {
            var html = Render("de");

            Assert.Contains("<html lang=\"de\">", html);
            Assert.Contains("Konferenzen durchsuchen", html);
            Assert.Contains("Konferenzen durchsuchen...", html);
        }

        [Fact]
        public void Search_and_year_filter_narrow_conferences()
        {
            var conferences = new[]
            {
                new ConferenceDto { Title = "Chaos Communication Congress", Acronym = "CCC", UpdatedAt = new DateTime(2024, 1, 1) },
                new ConferenceDto { Title = "Chaos Camp", Acronym = "Camp", UpdatedAt = new DateTime(2023, 1, 1) },
                new ConferenceDto { Title = "Other", Acronym = "CCC2", UpdatedAt = new DateTime(2024, 1, 1) }
            };

            var filtered = BrowsePageHtml.FilterConferences(conferences, "chaos", "2024");

            Assert.Single(filtered);
            Assert.Equal("CCC", filtered[0].Acronym);
        }

        [Fact]
        public void Selected_conference_renders_event_actions()
        {
            var conference = new ConferenceDto { Title = "Congress", Acronym = "c3" };
            var html = BrowsePageHtml.Render(
                "User",
                new[] { conference },
                null,
                null,
                conference,
                new[] { new EventDto { Guid = "event-guid", Title = "Opening talk" } },
                false,
                null,
                false,
                "en",
                Translations.For("en"));

            Assert.Contains("Conference events: Congress", html);
            Assert.Contains("Opening talk", html);
            Assert.Contains("action=\"/media_ccc/browse/add?lang=en\"", html);
            Assert.Contains("name=\"eventGuid\" value=\"event-guid\"", html);
        }

        [Fact]
        public void Dynamic_values_are_html_encoded()
        {
            var payload = "<script>alert(1)</script>";
            var html = BrowsePageHtml.Render(
                "User",
                new[] { new ConferenceDto { Title = payload, Acronym = "safe" } },
                null,
                null,
                null,
                null,
                false,
                null,
                false,
                "en",
                Translations.For("en"));

            Assert.DoesNotContain(payload, html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        }

        [Fact]
        public void Empty_state_and_error_state_are_rendered()
        {
            var empty = BrowsePageHtml.Render(
                "User", Array.Empty<ConferenceDto>(), null, null, null, null, false, null, false, "en", Translations.For("en"));
            var error = BrowsePageHtml.Render(
                "User", null, null, null, null, null, true, null, false, "en", Translations.For("en"));

            Assert.Contains(Translations.For("en")["browse.empty.conferences"], empty);
            Assert.Contains(Translations.For("en")["browse.error.conferences"], error);
        }

        private static string Render(string language)
        {
            return BrowsePageHtml.Render(
                "User",
                new List<ConferenceDto>(),
                null,
                null,
                null,
                null,
                false,
                null,
                false,
                language,
                Translations.For(language));
        }
    }
}
