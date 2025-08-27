using Microsoft.EntityFrameworkCore;
using VideoNest.Data;
using VideoNest.Models;

namespace VideoNest.Repositories {
    public class VideoRepository : IVideoRepository {
        private readonly VideoDbContext _context;

        public VideoRepository(VideoDbContext context) {
            _context = context;
        }

        public async Task SaveVideoAsync(VideoDB video) {
            video.CreatedAt = DateTime.UtcNow; // Define a data de criação
            await _context.Videos.AddAsync(video);
            await _context.SaveChangesAsync();
        }

        public async Task<VideoDB> GetVideoByIdAsync(int id) {
            return await _context.Videos.FindAsync(id);
        }
    }
}