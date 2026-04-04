using System;
using System.Collections.Generic;
using Jellyfin.Plugin.MediaCccDe.Services;
using Xunit;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class LanguageSelectorTests
    {
        private readonly ILanguageSelector _selector;

        public LanguageSelectorTests()
        {
            _selector = new LanguageSelector();
        }

        #region SelectBestLanguage Tests

        [Fact]
        public void SelectBestLanguage_returns_first_match_from_preferences()
        {
            // Arrange
            var availableLanguages = new List<string> { "en", "de", "es" };
            var preferences = new List<string> { "de", "en" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("de", result);
        }

        [Fact]
        public void SelectBestLanguage_returns_original_if_no_preference_matches()
        {
            // Arrange
            var availableLanguages = new List<string> { "fr", "it" };
            var preferences = new List<string> { "en", "de" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("original", result);
        }

        [Fact]
        public void SelectBestLanguage_is_case_insensitive()
        {
            // Arrange
            var availableLanguages = new List<string> { "EN", "DE", "es" };
            var preferences = new List<string> { "en", "de" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("EN", result); // Returns original case from available
        }

        [Fact]
        public void SelectBestLanguage_handles_empty_preferences()
        {
            // Arrange
            var availableLanguages = new List<string> { "en", "de" };
            var preferences = new List<string>();

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("en", result); // Returns first available or "original"
        }

        [Fact]
        public void SelectBestLanguage_handles_empty_preferences_with_original()
        {
            // Arrange
            var availableLanguages = new List<string> { "original" };
            var preferences = new List<string>();

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("original", result);
        }

        [Fact]
        public void SelectBestLanguage_handles_no_available_languages()
        {
            // Arrange
            var availableLanguages = new List<string>();
            var preferences = new List<string> { "en", "de" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("original", result);
        }

        [Fact]
        public void SelectBestLanguage_handles_null_available_languages()
        {
            // Arrange
            List<string> availableLanguages = null;
            var preferences = new List<string> { "en" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("original", result);
        }

        [Fact]
        public void SelectBestLanguage_handles_null_preferences()
        {
            // Arrange
            var availableLanguages = new List<string> { "en", "de" };
            List<string> preferences = null;

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("en", result); // Returns first available or "original"
        }

        #endregion

        #region IsLanguageMatch Tests

        [Fact]
        public void IsLanguageMatch_matches_exact_code()
        {
            // Arrange
            var languageCode = "en";
            var preference = "en";

            // Act
            var result = _selector.IsLanguageMatch(languageCode, preference);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsLanguageMatch_matches_with_country_code()
        {
            // "en" preference matches "en-US", "en-GB"
            
            // Test 1: en matches en-US
            var result1 = _selector.IsLanguageMatch("en-US", "en");
            Assert.True(result1);

            // Test 2: en matches en-GB
            var result2 = _selector.IsLanguageMatch("en-GB", "en");
            Assert.True(result2);

            // Test 3: en matches en-us (case insensitive)
            var result3 = _selector.IsLanguageMatch("en-us", "EN");
            Assert.True(result3);
        }

        [Fact]
        public void IsLanguageMatch_is_case_insensitive()
        {
            // Arrange & Act & Assert
            Assert.True(_selector.IsLanguageMatch("EN", "en"));
            Assert.True(_selector.IsLanguageMatch("en", "EN"));
            Assert.True(_selector.IsLanguageMatch("En", "eN"));
            Assert.True(_selector.IsLanguageMatch("EN-US", "en"));
            Assert.True(_selector.IsLanguageMatch("en-us", "EN"));
        }

        [Fact]
        public void IsLanguageMatch_handles_three_letter_codes()
        {
            // ISO 639-2/T codes (three letters)
            Assert.True(_selector.IsLanguageMatch("eng", "eng"));
            Assert.False(_selector.IsLanguageMatch("eng", "en")); // No partial match
        }

        [Fact]
        public void IsLanguageMatch_does_not_match_different_languages()
        {
            // Arrange & Act & Assert
            Assert.False(_selector.IsLanguageMatch("en", "de"));
            Assert.False(_selector.IsLanguageMatch("en-US", "de"));
            Assert.False(_selector.IsLanguageMatch("fr", "en"));
        }

        #endregion

        #region SelectBestAudioLanguage Tests

        [Fact]
        public void SelectBestAudioLanguage_returns_best_audio()
        {
            // Arrange
            var availableLanguages = new List<string> { "en", "de", "es" };
            var preferences = new List<string> { "de", "en" };

            // Act
            var result = _selector.SelectBestAudioLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("de", result);
        }

        [Fact]
        public void SelectBestAudioLanguage_falls_back_to_original()
        {
            // Arrange
            var availableLanguages = new List<string> { "fr", "it" };
            var preferences = new List<string> { "en", "de" };

            // Act
            var result = _selector.SelectBestAudioLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("original", result);
        }

        [Fact]
        public void SelectBestAudioLanguage_prefers_preferences_over_original()
        {
            // Arrange
            var availableLanguages = new List<string> { "original", "de", "en" };
            var preferences = new List<string> { "en", "de" };

            // Act
            var result = _selector.SelectBestAudioLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("en", result); // Not "original", but "en" which is in preferences
        }

        #endregion

        #region SelectBestSubtitleLanguage Tests

        [Fact]
        public void SelectBestSubtitleLanguage_returns_best_subtitle()
        {
            // Arrange
            var availableLanguages = new List<string> { "en", "de", "es" };
            var preferences = new List<string> { "de", "en" };

            // Act
            var result = _selector.SelectBestSubtitleLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("de", result);
        }

        [Fact]
        public void SelectBestSubtitleLanguage_falls_back_to_none()
        {
            // Subtitles typically return null/empty when no match
            // Arrange
            var availableLanguages = new List<string> { "fr", "it" };
            var preferences = new List<string> { "en", "de" };

            // Act
            var result = _selector.SelectBestSubtitleLanguage(availableLanguages, preferences);

            // Assert
            Assert.Null(result); // No suitable subtitle
        }

        [Fact]
        public void SelectBestSubtitleLanguage_returns_null_for_empty_available()
        {
            // Arrange
            var availableLanguages = new List<string>();
            var preferences = new List<string> { "en" };

            // Act
            var result = _selector.SelectBestSubtitleLanguage(availableLanguages, preferences);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void SelectBestSubtitleLanguage_returns_null_for_null_available()
        {
            // Arrange
            List<string> availableLanguages = null;
            var preferences = new List<string> { "en" };

            // Act
            var result = _selector.SelectBestSubtitleLanguage(availableLanguages, preferences);

            // Assert
            Assert.Null(result);
        }

        #endregion

        #region Edge Case Tests

        [Fact]
        public void SelectBestLanguage_prefers_exact_match_over_partial()
        {
            // Available languages: ["de", "de-DE"] - preference ["en", "de"] → returns "de"
            var availableLanguages = new List<string> { "de", "de-DE" };
            var preferences = new List<string> { "en", "de" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("de", result); // Exact match "de", not "de-DE"
        }

        [Fact]
        public void SelectBestLanguage_returns_original_when_only_original_available()
        {
            // Available languages: [] - returns "original"
            var availableLanguages = new List<string>();
            var preferences = new List<string> { "en", "de" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("original", result);
        }

        [Fact]
        public void SelectBestLanguage_handles_undefined_language()
        {
            // Available languages: ["und"] (undefined) - matches as "original"
            var availableLanguages = new List<string> { "und" };
            var preferences = new List<string> { "en" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("und", result); // "und" is treated as original
        }

        [Fact]
        public void SelectBestLanguage_handles_multiple_region_codes()
        {
            // Arrange - priority order test
            var availableLanguages = new List<string> { "en-GB", "en-US", "en-AU" };
            var preferences = new List<string> { "en" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            // Should return first matching language from available list
            Assert.Equal("en-GB", result);
        }

        [Fact]
        public void SelectBestLanguage_matches_regional_variant()
        {
            // Arrange - preference for specific region variant
            var availableLanguages = new List<string> { "en-US", "en-GB" };
            var preferences = new List<string> { "en-GB" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("en-GB", result);
        }

        [Fact]
        public void SelectBestLanguage_falls_back_to_base_language()
        {
            // Arrange
            var availableLanguages = new List<string> { "en-US", "de" };
            var preferences = new List<string> { "en" }; // No exact match, but "en-US" matches base

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("en-US", result); // Base "en" matches "en-US"
        }

        [Fact]
        public void SelectBestLanguage_preserves_original_case()
        {
            // Arrange
            var availableLanguages = new List<string> { "EN-US", "de" };
            var preferences = new List<string> { "en" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("EN-US", result); // Returns with original casing from available
        }

        [Fact]
        public void IsLanguageMatch_handles_complex_codes()
        {
            // Test various language code formats
            Assert.True(_selector.IsLanguageMatch("zh-Hans", "zh")); // Chinese simplified
            Assert.True(_selector.IsLanguageMatch("zh-Hant", "zh")); // Chinese traditional
            Assert.False(_selector.IsLanguageMatch("zh-Hans", "zh-Hant")); // Different variants
        }

        [Fact]
        public void SelectBestLanguage_with_mixed_formats()
        {
            // Arrange - mixed ISO 639-1 and 639-2 codes
            var availableLanguages = new List<string> { "eng", "deu", "es" };
            var preferences = new List<string> { "es", "de" };

            // Act
            var result = _selector.SelectBestLanguage(availableLanguages, preferences);

            // Assert
            Assert.Equal("es", result); // Only "es" matches, others are 3-letter codes
        }

        #endregion
    }
}