using Microsoft.AspNetCore.Http;

namespace VideoNest.DTO {
    /// <summary>
    /// DTO para request de upload de vídeo (FASE 02)
    /// </summary>
    public class VideoUploadRequest {
        /// <summary>
        /// Título do vídeo (opcional)
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Arquivo de vídeo (.mp4, .avi) - OBRIGATÓRIO
        /// </summary>
        public IFormFile? File { get; set; }

        /// <summary>
        /// Descrição do vídeo (opcional)
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Valida se o request está completo
        /// </summary>
        /// <returns>True se válido</returns>
        public bool IsValid() {
            return File != null && File.Length > 0;
        }
    }
}