using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Interface for managing per-user watchlist libraries.
    /// </summary>
    public interface IUserLibraryService
    {
        /// <summary>
        /// Gets or creates a user's watchlist library.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <param name="username">The username.</param>
        /// <returns>The user's library information.</returns>
        Task<UserLibrary> GetOrCreateUserLibraryAsync(Guid userId, string username);

        /// <summary>
        /// Checks if a user's watchlist library exists.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        /// <returns>True if the library exists, false otherwise.</returns>
        Task<bool> UserLibraryExistsAsync(Guid userId);

        /// <summary>
        /// Removes a user's watchlist library.
        /// </summary>
        /// <param name="userId">The user ID.</param>
        Task RemoveUserLibraryAsync(Guid userId);
    }
}
