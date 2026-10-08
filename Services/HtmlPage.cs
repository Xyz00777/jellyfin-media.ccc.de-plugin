using System.Net;
using System.Text;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Shared CSS contract: wrap/page/lede, fieldset/legend/label/hint/check, notice/error/code/who,
    /// grid/card, toolbar/list/status/badge/pill, muted, and language-switcher.
    /// </summary>
    internal static class HtmlPage
    {
        public static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        public static void Begin(StringBuilder html, string language, string title)
        {
            html.Append("<!DOCTYPE html><html lang=\"").Append(Escape(language)).Append("\"><head><meta charset=\"utf-8\" />");
            html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
            html.Append("<title>").Append(Escape(title)).Append("</title><style>");
            html.Append("body{font-family:system-ui,-apple-system,'Segoe UI',Roboto,sans-serif;background:#101010;color:#eee;margin:0;padding:2rem;}");
            html.Append(".wrap{max-width:64rem;margin:0 auto;}.page{max-width:44rem;margin:0 auto;}");
            html.Append("h1{font-size:1.4rem;margin:0 0 .25rem;}p.lede{color:#aaa;margin:0 0 1.5rem;}");
            html.Append("fieldset{border:1px solid #333;border-radius:.5rem;margin:0 0 1.25rem;padding:1rem 1.25rem;}");
            html.Append("legend{padding:0 .4rem;color:#bbb;font-size:.85rem;text-transform:uppercase;letter-spacing:.05em;}");
            html.Append("label{display:block;margin:.75rem 0 .25rem;font-weight:600;font-size:.9rem;}");
            html.Append("select,input[type=text],input[type=password],input[type=number]{width:100%;padding:.5rem;border-radius:.25rem;border:1px solid #444;background:#1c1c1c;color:#eee;box-sizing:border-box;}");
            html.Append(".hint{color:#999;font-size:.82rem;margin:.25rem 0 0;}.muted{color:#999;}");
            html.Append(".check{display:flex;gap:.5rem;align-items:flex-start;margin:.75rem 0 0;}.check input{margin-top:.2rem;}.check span{font-weight:600;font-size:.9rem;}");
            html.Append("button{margin-top:1rem;padding:.6rem 1.4rem;border:0;border-radius:.25rem;background:#00a4dc;color:#04121c;font-weight:700;font-size:.95rem;cursor:pointer;}");
            html.Append(".notice{background:#12351f;border:1px solid #1f6b3a;padding:.75rem 1rem;border-radius:.25rem;margin:0 0 1.25rem;}");
            html.Append(".error{background:#3a1414;border:1px solid #7a2a2a;padding:.75rem 1rem;border-radius:.25rem;margin:0 0 1.25rem;}");
            html.Append("code{background:#1c1c1c;padding:.15rem .35rem;border-radius:.2rem;word-break:break-all;}.who{color:#7fd48a;font-size:.9rem;margin:0 0 1.25rem;}");
            html.Append(".grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(15rem,1fr));gap:1rem;}.card{border:1px solid #333;border-radius:.5rem;padding:1rem;background:#171717;}");
            html.Append(".toolbar{display:flex;align-items:center;gap:.75rem;flex-wrap:wrap;margin:0 0 1rem;}.toolbar button{margin-top:0;}");
            html.Append(".list{width:100%;border-collapse:collapse;}.list th,.list td{text-align:left;padding:.65rem;border-bottom:1px solid #333;}.list th{color:#bbb;}");
            html.Append(".status{white-space:nowrap;}.badge,.pill{display:inline-block;padding:.2rem .55rem;border-radius:999px;background:#303030;color:#ddd;font-size:.8rem;}.language-switcher{display:flex;justify-content:flex-end;gap:.75rem;margin-bottom:1rem;font-size:.85rem;}.language-switcher a{color:#9bdcf2;}.language-switcher [aria-current=page]{font-weight:700;color:#fff;}");
            html.Append("</style></head><body>");
        }

        public static void RenderLanguageSwitcher(StringBuilder html, string currentPath, string language, Translations translations)
        {
            html.Append("<nav class=\"language-switcher\" aria-label=\"").Append(Escape(translations["chrome.language"])).Append("\">");
            foreach (var supported in PluginLanguage.SupportedLanguages)
            {
                html.Append("<a href=\"").Append(Escape(currentPath)).Append("?lang=").Append(supported).Append('"');
                if (supported == language)
                {
                    html.Append(" aria-current=\"page\"");
                }
                html.Append('>').Append(Escape(PluginLanguage.DisplayName(supported))).Append("</a>");
            }
            html.Append("</nav>");
        }

        public static void End(StringBuilder html) => html.Append("</body></html>");
    }
}
