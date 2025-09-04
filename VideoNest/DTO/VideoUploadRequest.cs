namespace VideoNest.DTO {
    public class VideoUploadRequest {
        public string? Title { get; set; }
        public IFormFile? File { get; set; }
        public string? Description { get; set; }
    }
}