using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Interface for managing the download queue.
    /// </summary>
    public interface IDownloadQueue
    {
        /// <summary>
        /// Adds an item to the download queue.
        /// </summary>
        Task EnqueueAsync(DownloadQueueItem item);

        /// <summary>
        /// Gets and removes the next pending item from the queue.
        /// </summary>
        Task<DownloadQueueItem?> DequeueAsync();

        /// <summary>
        /// Gets a specific queue item by ID.
        /// </summary>
        Task<DownloadQueueItem?> GetItemAsync(Guid id);

        /// <summary>
        /// Gets all queue items for a specific user.
        /// </summary>
        Task<IEnumerable<DownloadQueueItem>> GetUserQueueAsync(Guid userId);

        /// <summary>
        /// Marks a queue item as in progress.
        /// </summary>
        Task MarkInProgressAsync(Guid id);

        /// <summary>
        /// Marks a queue item as completed.
        /// </summary>
        Task MarkCompletedAsync(Guid id);

        /// <summary>
        /// Marks a queue item as failed with an error message.
        /// </summary>
        Task MarkFailedAsync(Guid id, string errorMessage);

        /// <summary>
        /// Updates the download progress for a queue item.
        /// </summary>
        Task UpdateProgressAsync(Guid id, double progress);

        /// <summary>
        /// Gets the total number of items in the queue.
        /// </summary>
        Task<int> GetQueueLengthAsync();

        /// <summary>
        /// Removes an item from the queue.
        /// </summary>
        Task RemoveAsync(Guid id);
    }
}