using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Service for selecting the best language based on user preferences.
    /// </summary>
    public interface ILanguageSelector
    {
        /// <summary>
        /// Selects the best language from available languages based on user preferences.
        /// </summary>
        /// <param name="availableLanguages">List of available language codes.</param>
        /// <param name="preferences">User's language preferences in priority order.</param>
        /// <returns>Best matching language code, or "original" if no match found.</returns>
        string SelectBestLanguage(List<string>? availableLanguages, List<string>? preferences);

        /// <summary>
        /// Checks if a language code matches a preference.
        /// </summary>
        /// <param name="languageCode">The language code to check.</param>
        /// <param name="preference">The preference to match against.</param>
        /// <returns>True if the language matches the preference.</returns>
        bool IsLanguageMatch(string languageCode, string preference);

        /// <summary>
        /// Selects the best audio language from available languages.
        /// </summary>
        /// <param name="availableLanguages">List of available audio languages.</param>
        /// <param name="preferences">User's language preferences.</param>
        /// <returns>Best matching language code, or "original" as fallback.</returns>
        string SelectBestAudioLanguage(List<string>? availableLanguages, List<string>? preferences);

        /// <summary>
        /// Selects the best subtitle language from available languages.
        /// </summary>
        /// <param name="availableLanguages">List of available subtitle languages.</param>
        /// <param name="preferences">User's language preferences.</param>
        /// <returns>Best matching language code, or null if no match found.</returns>
        string? SelectBestSubtitleLanguage(List<string>? availableLanguages, List<string>? preferences);
    }

    /// <summary>
    /// Default implementation of language selection logic.
    /// </summary>
    public class LanguageSelector : ILanguageSelector
    {
        public string SelectBestLanguage(List<string>? availableLanguages, List<string>? preferences)
        {
            if (availableLanguages == null || availableLanguages.Count == 0)
            {
                return "original";
            }

            if (preferences == null || preferences.Count == 0)
            {
                return availableLanguages.Count > 0 ? availableLanguages[0] : "original";
            }

            foreach (var preference in preferences)
            {
                foreach (var available in availableLanguages)
                {
                    if (IsLanguageMatch(available, preference))
                    {
                        return available;
                    }
                }
            }

            // Check if "und" (undefined) is available - treat it as a valid fallback
            var undMatch = availableLanguages.FirstOrDefault(a => 
                string.Equals(a, "und", StringComparison.OrdinalIgnoreCase));
            if (undMatch != null)
            {
                return undMatch;
            }

            return "original";
        }

        public bool IsLanguageMatch(string languageCode, string preference)
        {
            if (string.IsNullOrEmpty(languageCode) || string.IsNullOrEmpty(preference))
            {
                return false;
            }

            var codeLower = languageCode.ToLowerInvariant();
            var prefLower = preference.ToLowerInvariant();

            // Exact match (case-insensitive)
            if (codeLower == prefLower)
            {
                return true;
            }

            // Handle "und" (undefined) as equivalent to "original"
            if (codeLower == "und" && prefLower == "original")
            {
                return true;
            }
            if (prefLower == "und" && codeLower == "original")
            {
                return true;
            }

            // Check if language code starts with preference (e.g., "en-US" matches "en")
            if (codeLower.StartsWith(prefLower + "-", StringComparison.OrdinalIgnoreCase) ||
                codeLower.StartsWith(prefLower + "_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public string SelectBestAudioLanguage(List<string>? availableLanguages, List<string>? preferences)
        {
            return SelectBestLanguage(availableLanguages, preferences);
        }

        public string? SelectBestSubtitleLanguage(List<string>? availableLanguages, List<string>? preferences)
        {
            if (availableLanguages == null || availableLanguages.Count == 0)
            {
                return null;
            }

            if (preferences == null || preferences.Count == 0)
            {
                return null;
            }

            foreach (var preference in preferences)
            {
                foreach (var available in availableLanguages)
                {
                    if (IsLanguageMatch(available, preference))
                    {
                        return available;
                    }
                }
            }

            return null;
        }
    }
}