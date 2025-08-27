// Arquivo: VideoNest/Services/IVideoService.cs
using VideoNest.DTO;
using VideoNest.Models;

namespace VideoNest.Services {
    public interface IVideoService {
        
        Task<int> CreateVideoAsync(VideoUploadRequest request);

        Task <VideoDB> BuscaVideoPorIDService(int id);
    }
}