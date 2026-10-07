using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Interface for managing user-specific data.
    /// </summary>
    public interface IUserDataManager
    {
        /// <summary>
        /// Gets user data for the specified user.
        /// </summary>
        UserData GetUserData(Guid userId);

        /// <summary>
        /// Saves user data for the specified user.
        /// </summary>
        void SaveUserData(Guid userId, UserData data);

        /// <summary>
        /// Adds an event to the user's watchlist.
        /// </summary>
        void AddToWatchlist(Guid userId, string eventGuid);

        /// <summary>
        /// Removes an event from the user's watchlist.
        /// </summary>
        void RemoveFromWatchlist(Guid userId, string eventGuid);

        /// <summary>
        /// Checks if an event is on the user's watchlist.
        /// </summary>
        bool IsOnWatchlist(Guid userId, string eventGuid);

        /// <summary>
        /// Marks an event as searched.
        /// </summary>
        void MarkAsSearched(Guid userId, string eventGuid);

        /// <summary>
        /// Checks if an event has been marked as searched.
        /// </summary>
        bool IsMarkedAsSearched(Guid userId, string eventGuid);

        /// <summary>
        /// Gets the user's watchlist.
        /// </summary>
        List<string> GetWatchlist(Guid userId);

        /// <summary>
        /// Sets the user's preferred audio languages.
        /// </summary>
        void SetPreferredAudioLanguages(Guid userId, List<string> languages);

        /// <summary>
        /// Gets the user's preferred audio languages.
        /// </summary>
        List<string> GetPreferredAudioLanguages(Guid userId);

        /// <summary>
        /// Sets the user's preferred subtitle languages.
        /// </summary>
        void SetPreferredSubtitleLanguages(Guid userId, List<string> languages);

        /// <summary>
        /// Gets the user's preferred subtitle languages.
        /// </summary>
        List<string> GetPreferredSubtitleLanguages(Guid userId);

        /// <summary>
        /// Persists user data to disk.
        /// </summary>
        Task PersistAsync(Guid userId);

        /// <summary>
        /// Loads user data from disk.
        /// </summary>
        Task LoadAsync(Guid userId);

        Task EnsureLoadedAsync(Guid userId);
    }
}
