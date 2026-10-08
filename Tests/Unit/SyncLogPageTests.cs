using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.MediaCccDe.Controllers;
using Jellyfin.Plugin.MediaCccDe.Services;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class SyncLogPageTests
    {
        [Fact]
        public void English_page_uses_English_chrome()
        {
            var html = SyncLogPageHtml.Render(Array.Empty<SyncLogEntry>(), "en", Translations.For("en"));

            Assert.Contains("<html lang=\"en\">", html);
            Assert.Contains("Sync History", html);
            Assert.Contains("Conference archive synchronization log", html);
            Assert.Contains("Trigger Manual Sync", html);
            Assert.Contains("No synchronization history yet.", html);
        }

        [Fact]
        public void German_page_uses_German_chrome()
        {
            var html = SyncLogPageHtml.Render(Array.Empty<SyncLogEntry>(), "de", Translations.For("de"));

            Assert.Contains("<html lang=\"de\">", html);
            Assert.Contains("Synchronisationsverlauf", html);
            Assert.Contains("Protokoll der Konferenzarchiv-Synchronisation", html);
            Assert.Contains("Synchronisation manuell starten", html);
            Assert.Contains("Noch kein Synchronisationsverlauf vorhanden.", html);
        }

        [Fact]
        public void History_entries_render_all_available_data()
        {
            var entry = new SyncLogEntry
            {
                Timestamp = new DateTime(2025, 4, 3, 12, 30, 45, DateTimeKind.Utc),
                ConferenceAcronym = "39C3",
                Status = SyncStatus.Failed,
                EventsProcessed = 12,
                FilesCreated = 7,
                ErrorMessage = "archive unavailable"
            };

            var html = SyncLogPageHtml.Render(new[] { entry }, "en", Translations.For("en"));

            Assert.Contains("39C3", html);
            Assert.Contains("Failed", html);
            Assert.Contains("2025-04-03 12:30:45", html);
            Assert.Contains(">12<", html);
            Assert.Contains(">7<", html);
            Assert.Contains("archive unavailable", html);
        }

        [Fact]
        public void Empty_history_renders_empty_state()
        {
            var html = SyncLogPageHtml.Render(Array.Empty<SyncLogEntry>(), "en", Translations.For("en"));

            Assert.Contains("No synchronization history yet.", html);
        }

        [Fact]
        public void Unavailable_history_renders_loading_state()
        {
            var html = SyncLogPageHtml.Render(null, "en", Translations.For("en"));

            Assert.Contains("Loading synchronization history...", html);
        }

        [Fact]
        public void Clear_confirmation_uses_catalog_question_and_post_form()
        {
            var html = SyncLogPageHtml.RenderClearConfirmation("en", Translations.For("en"));

            Assert.Contains("Clear all synchronization history? This cannot be undone.", html);
            Assert.Contains("method=\"post\" action=\"/media_ccc/sync/history/confirm\"", html);
            Assert.DoesNotContain("<script", html);
        }

        [Fact]
        public void Dynamic_history_values_are_html_escaped()
        {
            var entry = new SyncLogEntry
            {
                Timestamp = DateTime.UnixEpoch,
                ConferenceAcronym = "<script>alert(1)</script>",
                ErrorMessage = "<script>alert(2)</script>"
            };

            var html = SyncLogPageHtml.Render(new[] { entry }, "en", Translations.For("en"));

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
            Assert.Contains("&lt;script&gt;alert(2)&lt;/script&gt;", html);
        }

        [Fact]
        public void Existing_sync_json_routes_and_authorization_remain_present()
        {
            Assert.Equal("media_ccc/sync", typeof(SyncController).GetCustomAttributes(typeof(RouteAttribute), true)
                .Cast<RouteAttribute>().Single().Template);

            Assert.Equal(Policies.RequiresElevation, typeof(SyncController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
                .Cast<AuthorizeAttribute>().Single().Policy);
            AssertRoute<Microsoft.AspNetCore.Mvc.HttpPostAttribute>(nameof(SyncController.TriggerSync), "trigger");
            AssertRoute<HttpGetAttribute>(nameof(SyncController.GetSyncStatus), "status");
            AssertRoute<HttpGetAttribute>(nameof(SyncController.GetSyncHistory), "history");
            AssertRoute<Microsoft.AspNetCore.Mvc.HttpDeleteAttribute>(nameof(SyncController.ClearSyncHistory), "history");
        }

        private static void AssertRoute<TAttribute>(string methodName, string expectedTemplate)
            where TAttribute : HttpMethodAttribute
        {
            var method = typeof(SyncController).GetMethod(methodName);
            Assert.NotNull(method);
            var route = method!.GetCustomAttributes(typeof(TAttribute), true).Cast<TAttribute>().Single();
            Assert.Equal(expectedTemplate, route.Template);
        }
    }
}
