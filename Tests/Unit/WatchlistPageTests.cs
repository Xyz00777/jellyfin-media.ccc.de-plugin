using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Models;
using Jellyfin.Plugin.MediaCccDe.Services;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class WatchlistPageTests
    {
        [Theory]
        [InlineData("en", "<html lang=\"en\">", "My Watchlist", "Title")]
        [InlineData("de", "<html lang=\"de\">", "Meine Merkliste", "Titel")]
        public void Render_uses_selected_language(string language, string lang, string title, string tableTitle)
        {
            var html = WatchlistPageHtml.Render(
                "User",
                new[] { new WatchlistPageItem("event-guid", "Talk", "37C3") },
                Array.Empty<DownloadQueueItem>(),
                null,
                language,
                Translations.For(language));

            Assert.Contains(lang, html);
            Assert.Contains(title, html);
            Assert.Contains(tableTitle, html);
        }

        [Theory]
        [InlineData(DownloadStatus.Pending, "pending", "Ausstehend")]
        [InlineData(DownloadStatus.InProgress, "downloading", "Wird heruntergeladen")]
        [InlineData(DownloadStatus.Completed, "ready", "Bereit")]
        [InlineData(DownloadStatus.Failed, "failed", "Fehlgeschlagen")]
        public void Render_maps_statuses_to_localized_labels(DownloadStatus status, string english, string german)
        {
            var item = new WatchlistPageItem("event-guid", "Talk", "37C3");
            var queueItem = new DownloadQueueItem { EventGuid = item.Guid, Status = status, Progress = 0.42 };

            var englishHtml = WatchlistPageHtml.Render("User", new[] { item }, new[] { queueItem }, null, "en", Translations.For("en"));
            var germanHtml = WatchlistPageHtml.Render("User", new[] { item }, new[] { queueItem }, null, "de", Translations.For("de"));

            Assert.Contains(Translations.For("en")["watchlist.status." + english], englishHtml);
            Assert.Contains(german, germanHtml);
        }

        [Fact]
        public void Render_shows_empty_state_when_watchlist_is_empty()
        {
            var html = WatchlistPageHtml.Render("User", Array.Empty<WatchlistPageItem>(), Array.Empty<DownloadQueueItem>(), null, "en", Translations.For("en"));

            Assert.Contains("Your watchlist is empty", html);
            Assert.Contains("Browse conferences and add events", html);
        }

        [Fact]
        public void Render_shows_remove_confirmation_text_without_javascript()
        {
            var html = WatchlistPageHtml.Render(
                "User",
                new[] { new WatchlistPageItem("event-guid", "Talk", "37C3") },
                Array.Empty<DownloadQueueItem>(),
                null,
                "en",
                Translations.For("en"));

            Assert.Contains("Remove this item from your watchlist? The downloaded file will be deleted.", html);
            Assert.Contains("method=\"post\"", html);
            Assert.DoesNotContain("<script", html);
        }

        [Fact]
        public void Render_escapes_dynamic_values()
        {
            var payload = "<script>alert(1)</script>";
            var html = WatchlistPageHtml.Render(
                "User",
                new[] { new WatchlistPageItem("event-guid", payload, payload) },
                Array.Empty<DownloadQueueItem>(),
                null,
                "en",
                Translations.For("en"));

            Assert.DoesNotContain(payload, html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        }
    }
}
