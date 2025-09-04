// Arquivo: VideoNest/Services/IVideoService.cs
using VideoNest.DTO;
using VideoNest.Models;

namespace VideoNest.Services {
    public interface IVideoService {
                
        Task<VideoDB> BuscaVideoPorIDService(int id); //Busca informaççoes por ID
      
        Task<int> UploadVideoAsync(IFormFile file, VideoUploadRequest request); //Upload video file
    }
}