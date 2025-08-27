using Microsoft.EntityFrameworkCore.Metadata.Internal;
using VideoNest.DTO;
using VideoNest.Models;
using VideoNest.Repositories;

namespace VideoNest.Services {
    public class VideoService : IVideoService {
        private readonly IVideoRepository _videoRepository;

        public VideoService(IVideoRepository videoRepository) {
            _videoRepository = videoRepository;
        }

        public async Task<int> CreateVideoAsync(VideoUploadRequest request) {
            // Mapear o DTO para a entidade
            var video = new VideoDB {
                Title = request.Title,
                Description = request.Description,
                Duration = request.Duration
            };

            // Salvar no banco via repositório
            await _videoRepository.SaveVideoAsync(video);

            return video.Id;
        }

        public async Task<VideoDB> BuscaVideoPorIDService(int id) {
            // implementação real
            return await _videoRepository.GetVideoByIdAsync(id);
        }
    }
}
