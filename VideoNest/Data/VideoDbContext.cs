using Microsoft.EntityFrameworkCore;
using VideoNest.Models;

namespace VideoNest.Data {
    public class VideoDbContext : DbContext {
        public VideoDbContext(DbContextOptions<VideoDbContext> options) : base(options) {
        }

        public DbSet<VideoDB> Videos { get; set; }
        public DbSet<QRCodeResult> QRCodes { get; set; }
    }
}