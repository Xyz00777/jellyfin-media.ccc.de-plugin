using System;
using System.Linq;
using Xunit;
using Jellyfin.Plugin.MediaCccDe.Services;

namespace Jellyfin.Plugin.MediaCccDe.Tests.Unit
{
    public class StrmHelperTests
    {
        #region ExtractDayNumber Tests

        [Fact]
        public void ExtractDayNumber_preserves_december_congress_day_number()
        {
            // Dec 27 = Day 1, Dec 28 = Day 2, Dec 29 = Day 3 (CCC convention)
            Assert.Equal(1, StrmHelper.ExtractDayNumber("2023-12-27"));
            Assert.Equal(2, StrmHelper.ExtractDayNumber("2023-12-28"));
            Assert.Equal(3, StrmHelper.ExtractDayNumber("2023-12-29"));
            Assert.Equal(4, StrmHelper.ExtractDayNumber("2023-12-30"));
            Assert.Equal(5, StrmHelper.ExtractDayNumber("2023-12-31"));
        }

        [Fact]
        public void ExtractDayNumber_works_for_july_conference()
        {
            // July conference without conference start date — should use day-of-month
            // (matches the DeriveParentIndexNumber fix from P2-2)
            Assert.Equal(5, StrmHelper.ExtractDayNumber("2024-07-05"));
            Assert.Equal(1, StrmHelper.ExtractDayNumber("2024-07-01"));
            Assert.Equal(15, StrmHelper.ExtractDayNumber("2024-07-15"));
        }

        [Fact]
        public void ExtractDayNumber_works_for_march_conference()
        {
            // March conference without conference start date — should use day-of-month
            Assert.Equal(22, StrmHelper.ExtractDayNumber("2024-03-22"));
            Assert.Equal(1, StrmHelper.ExtractDayNumber("2024-03-01"));
            Assert.Equal(31, StrmHelper.ExtractDayNumber("2024-03-31"));
        }

        [Fact]
        public void ExtractDayNumber_with_conference_start_date_computes_offset()
        {
            // When we know the conference start date, compute offset from that
            // A 3-day July conference starting on July 5:
            Assert.Equal(1, StrmHelper.ExtractDayNumber("2024-07-05", "2024-07-05"));
            Assert.Equal(2, StrmHelper.ExtractDayNumber("2024-07-06", "2024-07-05"));
            Assert.Equal(3, StrmHelper.ExtractDayNumber("2024-07-07", "2024-07-05"));

            // A 4-day March conference starting on March 22:
            Assert.Equal(1, StrmHelper.ExtractDayNumber("2024-03-22", "2024-03-22"));
            Assert.Equal(2, StrmHelper.ExtractDayNumber("2024-03-23", "2024-03-22"));
            Assert.Equal(3, StrmHelper.ExtractDayNumber("2024-03-24", "2024-03-22"));
            Assert.Equal(4, StrmHelper.ExtractDayNumber("2024-03-25", "2024-03-22"));
        }

        [Fact]
        public void ExtractDayNumber_with_conference_start_date_minimum_is_1()
        {
            // Events before the conference start date should still produce at least Day 1
            Assert.Equal(1, StrmHelper.ExtractDayNumber("2024-07-04", "2024-07-05"));
        }

        [Fact]
        public void ExtractDayNumber_with_conference_start_date_overrides_december_convention()
        {
            // When conference start date is known, even Dec conferences use it
            // CCC starting Dec 27 with known start date:
            Assert.Equal(1, StrmHelper.ExtractDayNumber("2023-12-27", "2023-12-27"));
            Assert.Equal(2, StrmHelper.ExtractDayNumber("2023-12-28", "2023-12-27"));
            Assert.Equal(4, StrmHelper.ExtractDayNumber("2023-12-30", "2023-12-27"));
        }

        [Fact]
        public void ExtractDayNumber_returns_null_for_null_date()
        {
            Assert.Null(StrmHelper.ExtractDayNumber(null));
        }

        [Fact]
        public void ExtractDayNumber_returns_null_for_empty_date()
        {
            Assert.Null(StrmHelper.ExtractDayNumber(""));
        }

        [Fact]
        public void ExtractDayNumber_returns_null_for_invalid_date()
        {
            Assert.Null(StrmHelper.ExtractDayNumber("not-a-date"));
        }

        #endregion

        #region SanitizeFileName Tests

        [Fact]
        public void SanitizeFileName_removes_invalid_chars()
        {
            // Test that characters invalid on Windows/Linux/macOS are removed
            var result = StrmHelper.SanitizeFileName("file<name>with:invalid*chars?\"test|");
            Assert.DoesNotContain("<", result);
            Assert.DoesNotContain(">", result);
            Assert.DoesNotContain(":", result);
            Assert.DoesNotContain("*", result);
            Assert.DoesNotContain("?", result);
            Assert.DoesNotContain("\"", result);
            Assert.DoesNotContain("|", result);
        }

        [Fact]
        public void SanitizeFileName_removes_path_separators()
        {
            var result = StrmHelper.SanitizeFileName("conf/test\\event");
            Assert.DoesNotContain("/", result);
            Assert.DoesNotContain("\\", result);
        }

        [Fact]
        public void SanitizeFileName_returns_unknown_for_null()
        {
            Assert.Equal("unknown", StrmHelper.SanitizeFileName(null!));
        }

        [Fact]
        public void SanitizeFileName_returns_unknown_for_empty()
        {
            Assert.Equal("unknown", StrmHelper.SanitizeFileName(""));
        }

        [Fact]
        public void SanitizeFileName_trims_dots_and_spaces()
        {
            // Trailing dots and spaces are invalid on Windows
            Assert.Equal("file", StrmHelper.SanitizeFileName("file.  "));
            Assert.Equal("file", StrmHelper.SanitizeFileName("file . "));
        }

        [Fact]
        public void SanitizeFileName_preserves_valid_characters()
        {
            // Letters, digits, hyphens, underscores should be preserved
            var result = StrmHelper.SanitizeFileName("37c3-opening-ceremony_keynote");
            Assert.Equal("37c3-opening-ceremony_keynote", result);
        }

        [Fact]
        public void SanitizeFileName_preserves_unicode()
        {
            var result = StrmHelper.SanitizeFileName("über-keynote");
            Assert.Equal("über-keynote", result);
        }

        #endregion

        #region NormalizeConferenceDirectory Tests

        [Fact]
        public void NormalizeConferenceDirectory_preserves_original_case()
        {
            // Conference directories should use the original case from the API
            var result = StrmHelper.NormalizeConferenceDirectory("37C3");
            Assert.Equal("37C3", result);
        }

        [Fact]
        public void NormalizeConferenceDirectory_sanitizes_acronym()
        {
            var result = StrmHelper.NormalizeConferenceDirectory("conf/test");
            Assert.DoesNotContain("/", result);
        }

        [Fact]
        public void NormalizeConferenceDirectory_returns_unknown_for_null()
        {
            Assert.Equal("unknown", StrmHelper.NormalizeConferenceDirectory(null!));
        }

        #endregion
    }
}