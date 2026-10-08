using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    internal static class PluginLanguage
    {
        public const string DefaultLanguage = "en";

        public static IReadOnlyList<string> SupportedLanguages { get; } = Array.AsReadOnly(new[] { "en", "de" });

        public static string Resolve(string? queryValue, string? acceptLanguage)
        {
            if (queryValue is not null)
            {
                return Normalize(queryValue);
            }

            if (!string.IsNullOrWhiteSpace(acceptLanguage))
            {
                foreach (var preference in acceptLanguage.Split(','))
                {
                    var candidate = preference.Split(';', 2)[0].Trim();
                    var resolved = Normalize(candidate);
                    if (!string.Equals(resolved, DefaultLanguage, StringComparison.Ordinal) ||
                        candidate.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    {
                        return resolved;
                    }
                }
            }

            return DefaultLanguage;
        }

        public static string Resolve(HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var queryValue = request.Query.TryGetValue("lang", out var language) ? language.ToString() : null;
            return Resolve(queryValue, request.Headers.AcceptLanguage.ToString());
        }

        public static string DisplayName(string language)
        {
            return Normalize(language) switch
            {
                "de" => "Deutsch",
                _ => "English"
            };
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultLanguage;
            }

            var primary = value.Trim().Split('-', '_', ';')[0];
            return primary.Equals("de", StringComparison.OrdinalIgnoreCase) ? "de" :
                primary.Equals("en", StringComparison.OrdinalIgnoreCase) ? "en" : DefaultLanguage;
        }
    }
}
