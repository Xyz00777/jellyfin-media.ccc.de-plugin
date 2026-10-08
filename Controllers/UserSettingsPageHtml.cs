using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;

namespace Jellyfin.Plugin.MediaCccDe.Controllers
{
    /// <summary>
    /// Renders the per-user language page without JavaScript. The visitor proves who they
    /// are once with a Jellyfin API key; after that the signed cookie identifies them.
    /// </summary>
    internal static class UserSettingsPageHtml
    {
        internal const string PagePath = "/media_ccc/settings/languages";

        private const int MaxLanguageEntries = 50;

        internal static string Render(
            string? userName,
            IReadOnlyList<string>? audioLanguages,
            IReadOnlyList<string>? subtitleLanguages,
            string? notice,
            string? error)
        {
            var html = new StringBuilder();

            html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\" />");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            html.Append("<title>Media.CCC.de Language Preferences</title>");
            html.Append("<style>");
            html.Append("body{font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;background:#101010;color:#eee;margin:0;padding:2rem;}");
            html.Append(".wrap{max-width:44rem;margin:0 auto;}");
            html.Append("h1{font-size:1.4rem;margin:0 0 .25rem;}");
            html.Append("p.lede{color:#aaa;margin:0 0 1.5rem;}");
            html.Append("fieldset{border:1px solid #333;border-radius:.5rem;margin:0 0 1.25rem;padding:1rem 1.25rem;}");
            html.Append("legend{padding:0 .4rem;color:#bbb;font-size:.85rem;text-transform:uppercase;letter-spacing:.05em;}");
            html.Append("label{display:block;margin:.75rem 0 .25rem;font-weight:600;font-size:.9rem;}");
            html.Append("input[type=text]{width:100%;padding:.5rem;border-radius:.25rem;border:1px solid #444;background:#1c1c1c;color:#eee;box-sizing:border-box;}");
            html.Append(".hint{color:#999;font-size:.82rem;margin:.25rem 0 0;}");
            html.Append("button{margin-top:1rem;padding:.6rem 1.4rem;border:0;border-radius:.25rem;background:#00a4dc;color:#04121c;font-weight:700;font-size:.95rem;cursor:pointer;}");
            html.Append(".notice{background:#12351f;border:1px solid #1f6b3a;padding:.75rem 1rem;border-radius:.25rem;margin:0 0 1.25rem;}");
            html.Append(".error{background:#3a1414;border:1px solid #7a2a2a;padding:.75rem 1rem;border-radius:.25rem;margin:0 0 1.25rem;}");
            html.Append(".who{color:#7fd48a;font-size:.9rem;margin:0 0 1.25rem;}");
            html.Append("code{background:#1c1c1c;padding:.15rem .35rem;border-radius:.2rem;}");
            html.Append("</style></head><body><div class=\"wrap\">");

            html.Append("<h1>Media.CCC.de Language Preferences</h1>");
            html.Append("<p class=\"lede\">Your own preferences, used when a talk you watchlist is downloaded for you.</p>");

            if (notice is not null)
            {
                html.Append("<div class=\"notice\">").Append(Escape(notice)).Append("</div>");
            }

            if (error is not null)
            {
                html.Append("<div class=\"error\">").Append(Escape(error)).Append("</div>");
            }

            if (userName is null)
            {
                RenderSignIn(html);
            }
            else
            {
                RenderForm(html, userName, audioLanguages ?? Array.Empty<string>(), subtitleLanguages ?? Array.Empty<string>());
            }

            html.Append("</div></body></html>");
            return html.ToString();
        }

        private static void RenderSignIn(StringBuilder html)
        {
            html.Append("<fieldset><legend>Identify yourself</legend>");
            html.Append("<p class=\"hint\">Jellyfin 12 cannot sign you in from this page, so confirm your account once with your own Jellyfin API key. Create one under <strong>Dashboard &gt; Advanced &gt; API Keys</strong>. The key is checked against your server and then discarded; it is never stored.</p>");
            html.Append("<form method=\"post\" action=\"").Append(PagePath).Append("/identify\">");
            html.Append("<label for=\"apikey\">Your Jellyfin API key</label>");
            html.Append("<input type=\"password\" id=\"apikey\" name=\"apikey\" autocomplete=\"off\" required />");
            html.Append("<button type=\"submit\">Continue</button>");
            html.Append("</form></fieldset>");
        }

        private static void RenderForm(StringBuilder html, string userName, IReadOnlyList<string> audio, IReadOnlyList<string> subtitles)
        {
            html.Append("<p class=\"who\">Signed in as <strong>").Append(Escape(userName)).Append("</strong></p>");

            html.Append("<form method=\"post\" action=\"").Append(PagePath).Append("\">");

            html.Append("<fieldset><legend>Languages</legend>");
            html.Append("<label for=\"audio\">Preferred audio languages</label>");
            html.Append("<input type=\"text\" id=\"audio\" name=\"audio\" value=\"").Append(Escape(Join(audio))).Append("\" />");
            html.Append("<p class=\"hint\">Comma-separated ISO 639-1 codes in order of preference, for example <code>en, de</code>. Used when a watchlist talk is downloaded for you.</p>");

            html.Append("<label for=\"subtitles\">Preferred subtitle languages</label>");
            html.Append("<input type=\"text\" id=\"subtitles\" name=\"subtitles\" value=\"").Append(Escape(Join(subtitles))).Append("\" />");
            html.Append("<p class=\"hint\">Used to pick a subtitle track when the administrator has enabled subtitle downloads.</p>");
            html.Append("</fieldset>");

            html.Append("<button type=\"submit\">Save</button>");
            html.Append("</form>");
        }

        internal static List<string> SplitLanguages(string? value)
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

                if (part.Length <= 16 && !result.Contains(part, StringComparer.OrdinalIgnoreCase))
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
