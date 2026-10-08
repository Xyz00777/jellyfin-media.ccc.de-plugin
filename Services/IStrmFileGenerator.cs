using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MediaCccDe.Models;

namespace Jellyfin.Plugin.MediaCccDe.Services
{
    /// <summary>
    /// Interface for generating individual .strm files.
    /// </summary>
    public interface IStrmFileGenerator
    {
        /// <summary>
        /// Generates a .strm file for a specific event.
        /// </summary>
        /// <param name="directory">Target directory for the .strm file.</param>
        /// <param name="event">Event data to generate .strm for.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Task representing the async operation.</returns>
        Task GenerateStrmAsync(string directory, EventDto @event, CancellationToken cancellationToken);
    }
}
