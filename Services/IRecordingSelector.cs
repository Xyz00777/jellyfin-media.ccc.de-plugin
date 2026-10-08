using System.Collections.Generic;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Selects the best recording from available options based on user preferences.
    /// </summary>
    public interface IRecordingSelector
    {
        /// <summary>
        /// Selects the best recording from a list based on user preferences.
        /// </summary>
        /// <param name="recordings">Available recordings to choose from.</param>
        /// <param name="preferences">User preferences for language, quality, and format.</param>
        /// <returns>The best matching recording, or null if no recordings available.</returns>
        Recording? SelectBestRecording(IEnumerable<Recording> recordings, RecordingPreferences? preferences);
    }
}
