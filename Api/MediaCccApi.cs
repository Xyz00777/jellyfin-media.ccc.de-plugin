using System.Net.Http;

namespace Jellyfin.Plugin.MediaCccDe.Api
{
    public class MediaCccApi
    {
        private readonly HttpClient _httpClient;

        public MediaCccApi(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
    }
}