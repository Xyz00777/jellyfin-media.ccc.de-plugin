using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    /// <summary>
    /// Renders the settings form entirely on the server. Nothing here depends on
    /// JavaScript, which Jellyfin 12 does not run for plugin pages.
    /// </summary>
    internal static class SettingsPageHtml
    {
        internal const string SettingsPath = "/media_ccc/settings";

        private const int MaxLanguageEntries = 50;

        internal static string Render(PluginConfiguration? configuration, string? notice, string? error)
        {
            var html = new StringBuilder();

            html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            html.Append("<title>Media.CCC.de Settings</title>");
            html.Append("<style>");
            html.Append("body{font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;background:#101010;color:#eee;margin:0;padding:2rem;}");
            html.Append(".wrap{max-width:44rem;margin:0 auto;}");
            html.Append("h1{font-size:1.4rem;margin:0 0 .25rem;}");
            html.Append("p.lede{color:#aaa;margin:0 0 1.5rem;}");
            html.Append("fieldset{border:1px solid #333;border-radius:.5rem;margin:0 0 1.25rem;padding:1rem 1.25rem;}");
            html.Append("legend{padding:0 .4rem;color:#bbb;font-size:.85rem;text-transform:uppercase;letter-spacing:.05em;}");
            html.Append("label{display:block;margin:.75rem 0 .25rem;font-weight:600;font-size:.9rem;}");
            html.Append("select,input[type=text],input[type=number]{width:100%;padding:.5rem;border-radius:.25rem;border:1px solid #444;background:#1c1c1c;color:#eee;box-sizing:border-box;}");
            html.Append(".hint{color:#999;font-size:.82rem;margin:.25rem 0 0;}");
            html.Append(".check{display:flex;gap:.5rem;align-items:flex-start;margin:.75rem 0 0;}");
            html.Append(".check input{margin-top:.2rem;}");
            html.Append(".check span{font-weight:600;font-size:.9rem;}");
            html.Append("button{margin-top:1rem;padding:.6rem 1.4rem;border:0;border-radius:.25rem;background:#00a4dc;color:#04121c;font-weight:700;font-size:.95rem;cursor:pointer;}");
            html.Append(".notice{background:#12351f;border:1px solid #1f6b3a;padding:.75rem 1rem;border-radius:.25rem;margin:0 0 1.25rem;}");
            html.Append(".error{background:#3a1414;border:1px solid #7a2a2a;padding:.75rem 1rem;border-radius:.25rem;margin:0 0 1.25rem;}");
            html.Append("code{background:#1c1c1c;padding:.15rem .35rem;border-radius:.2rem;word-break:break-all;}");
            html.Append("</style></head><body><div class=\"wrap\">");

            html.Append("<h1>Media.CCC.de Settings</h1>");
            html.Append("<p class=\"lede\">Served by the plugin itself, so it works on Jellyfin 12 where the dashboard does not run plugin scripts.</p>");

            if (notice is not null)
            {
                html.Append("<div class=\"notice\">").Append(Escape(notice)).Append("</div>");
            }

            if (error is not null)
            {
                html.Append("<div class=\"error\">").Append(Escape(error)).Append("</div>");
            }

            if (configuration is null)
            {
                RenderUnlock(html);
            }
            else
            {
                RenderForm(html, configuration);
            }

            html.Append("</div></body></html>");
            return html.ToString();
        }

        private static void RenderUnlock(StringBuilder html)
        {
            html.Append("<fieldset><legend>Administrator sign in</legend>");
            html.Append("<p class=\"hint\">The Jellyfin dashboard cannot authenticate plugin pages, so paste the access token printed in the server log. It is shown once per install and unlocks this form for 30 days.</p>");
            html.Append("<form method=\"post\" action=\"").Append(SettingsPath).Append("/unlock\">");
            html.Append("<label for=\"token\">Access token</label>");
            html.Append("<input type=\"text\" id=\"token\" name=\"token\" autocomplete=\"off\" required />");
            html.Append("<button type=\"submit\">Unlock</button>");
            html.Append("</form></fieldset>");
        }

        private static void RenderForm(StringBuilder html, PluginConfiguration configuration)
        {
            html.Append("<form method=\"post\" action=\"").Append(SettingsPath).Append("\">");

            html.Append("<fieldset><legend>Storage</legend>");
            html.Append("<label for=\"WatchlistPath\">Watchlist path</label>");
            html.Append("<input type=\"text\" id=\"WatchlistPath\" name=\"WatchlistPath\" value=\"").Append(Escape(configuration.WatchlistPath)).Append("\" />");
            html.Append("<p class=\"hint\">Directory for downloaded watchlist videos. Leave empty to use the plugin configuration directory.</p>");
            html.Append("</fieldset>");

            html.Append("<fieldset><legend>Synchronisation</legend>");
            html.Append("<label for=\"SyncIntervalHours\">Sync interval (hours)</label>");
            html.Append("<input type=\"number\" id=\"SyncIntervalHours\" name=\"SyncIntervalHours\" min=\"1\" max=\"168\" value=\"")
                .Append(configuration.SyncIntervalHours.ToString(CultureInfo.InvariantCulture)).Append("\" />");
            html.Append("<p class=\"hint\">How often to check for new content. 1-168 hours.</p>");

            html.Append("<label for=\"PreferredQuality\">Preferred quality</label>");
            html.Append("<select id=\"PreferredQuality\" name=\"PreferredQuality\">");
            foreach (var (value, label) in new[] { ("hd", "HD (high quality)"), ("sd", "SD (smaller files)") })
            {
                var selected = string.Equals(configuration.PreferredQuality, value, StringComparison.OrdinalIgnoreCase)
                    ? " selected"
                    : string.Empty;
                html.Append("<option value=\"").Append(value).Append('"').Append(selected).Append('>')
                    .Append(Escape(label)).Append("</option>");
            }
            html.Append("</select>");
            html.Append("<p class=\"hint\">Streaming prefers h264 mp4 for compatibility, and a file containing both of your preferred languages over either single-language file.</p>");
            html.Append("</fieldset>");

            html.Append("<fieldset><legend>Languages</legend>");
            html.Append("<label for=\"PreferredAudioLanguages\">Preferred audio languages</label>");
            html.Append("<input type=\"text\" id=\"PreferredAudioLanguages\" name=\"PreferredAudioLanguages\" value=\"")
                .Append(Escape(Join(configuration.PreferredAudioLanguages))).Append("\" />");
            html.Append("<p class=\"hint\">Comma-separated ISO 639-1 codes, in order of preference. Talks use the first available match, and a file containing both languages is preferred.</p>");

            html.Append("<label for=\"PreferredSubtitleLanguages\">Preferred subtitle languages</label>");
            html.Append("<input type=\"text\" id=\"PreferredSubtitleLanguages\" name=\"PreferredSubtitleLanguages\" value=\"")
                .Append(Escape(Join(configuration.PreferredSubtitleLanguages))).Append("\" />");
            html.Append("<p class=\"hint\">Comma-separated ISO 639-1 codes. Used to pick a subtitle when downloading them is enabled.</p>");
            html.Append("</fieldset>");

            html.Append("<fieldset><legend>Subtitles</legend>");
            html.Append("<div class=\"check\">");
            html.Append("<input type=\"checkbox\" id=\"DownloadSubtitles\" name=\"DownloadSubtitles\"")
                .Append(configuration.DownloadSubtitles ? " checked" : string.Empty).Append(" />");
            html.Append("<span>Download subtitles</span>");
            html.Append("</div>");
            html.Append("<p class=\"hint\">Off by default. Jellyfin finds subtitles only on local disk beside the media file, so each enabled talk fetches its subtitle next to the <code>.strm</code>. Existing libraries pick subtitles up on the next scan.</p>");
            html.Append("</fieldset>");

            html.Append("<button type=\"submit\">Save</button>");
            html.Append("</form>");
        }

        internal static List<string> SplitLanguages(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var result = new List<string>();

            foreach (var part in parts)
            {
                if (result.Count >= MaxLanguageEntries)
                {
                    break;
                }

                if (!result.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(part);
                }
            }

            return result;
        }

        private static string Join(IEnumerable<string> languages)
        {
            return string.Join(", ", languages);
        }

        private static string Escape(string? value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }
    }
}
