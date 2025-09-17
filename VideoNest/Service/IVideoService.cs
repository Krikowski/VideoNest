// Arquivo: VideoNest/Services/IVideoService.cs
using VideoNest.DTO;
using VideoNest.Models;

namespace VideoNest.Services {
    public interface IVideoService {
                
        Task<VideoDB> BuscaVideoPorIDService(int id); 
      
        Task<int> UploadVideoAsync(IFormFile file, VideoUploadRequest request); 
    }
}