using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using System.IO;

namespace VideoNest.DTO {
    /// <summary>
    /// DTO para request de upload de vídeo via multipart/form-data
    /// </summary>
    /// <remarks>
    /// Usado no endpoint POST /api/videos para receber arquivos .mp4/.avi e metadados.
    /// Compatível com IVideoService.UploadVideoAsync(IFormFile, VideoUploadRequest).
    /// </remarks>
    public class VideoUploadRequest {
        /// <summary>
        /// Título descritivo do vídeo (opcional)
        /// </summary>
        /// <example>Demonstração de QR Codes para Hackathon FIAP</example>
        [StringLength(100, ErrorMessage = "Título não pode exceder 100 caracteres")]
        public string? Title { get; set; }

        /// <summary>
        /// Arquivo de vídeo (.mp4, .avi) - OBRIGATÓRIO
        /// </summary>
        /// <remarks>
        /// Máximo 100MB. Validação de extensão e tamanho feita no Controller.
        /// </remarks>
        [Required(ErrorMessage = "Arquivo de vídeo é obrigatório")]
        public IFormFile? File { get; set; }

        /// <summary>
        /// Descrição detalhada do conteúdo do vídeo (opcional)
        /// </summary>
        /// <example>Vídeo de 2 minutos contendo 3 QR Codes em timestamps 15s, 45s e 90s</example>
        [StringLength(500, ErrorMessage = "Descrição não pode exceder 500 caracteres")]
        public string? Description { get; set; }

        /// <summary>
        /// Valida se o request está completo para upload
        /// </summary>
        /// <returns>True se válido para processamento</returns>
        /// <remarks>
        /// Verifica presença e tamanho mínimo do arquivo.
        /// Extensão e tamanho máximo são validados no Controller.
        /// </remarks>
        public bool IsValid() {
            return File != null && File.Length > 0;
        }

        /// <summary>
        /// Validação completa com mensagem de erro detalhada
        /// </summary>
        /// <param name="errorMessage">Mensagem de erro se inválido</param>
        /// <returns>True se válido, false se inválido</returns>
        public bool TryValidate(out string? errorMessage) {
            if (!IsValid()) {
                errorMessage = "Arquivo de vídeo é obrigatório e deve ter conteúdo";
                return false;
            }

            if (File!.Length > VideoConstants.MaxFileSizeBytes) {
                var maxSizeMB = VideoConstants.MaxFileSizeBytes / (1024 * 1024);
                errorMessage = $"Arquivo excede o tamanho máximo de {maxSizeMB}MB";
                return false;
            }

            var extension = Path.GetExtension(File.FileName)?.ToLowerInvariant();
            if (!VideoConstants.AllowedExtensions.Contains(extension)) {
                errorMessage = $"Formato inválido. Aceitos: {string.Join(", ", VideoConstants.AllowedExtensions)}";
                return false;
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Extrai metadados do request para logging e persistência
        /// </summary>
        /// <returns>Metadados formatados</returns>
        public VideoUploadMetadata GetMetadata() {
            return new VideoUploadMetadata {
                Title = Title ?? VideoConstants.DefaultTitle,
                Description = Description,
                FileName = Path.GetFileNameWithoutExtension(File?.FileName) ?? "unknown",
                FileExtension = Path.GetExtension(File?.FileName),
                FileSizeBytes = File?.Length ?? 0,
                ContentType = File?.ContentType ?? "application/octet-stream",
                UploadedAt = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Metadados do upload para logging e auditoria
    /// </summary>
    public class VideoUploadMetadata {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ContentType { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }

    /// <summary>
    /// Constantes específicas para upload de vídeo
    /// </summary>
    public static class VideoConstants {
        /// <summary>
        /// Título padrão para vídeos sem título especificado
        /// </summary>
        public const string DefaultTitle = "Vídeo sem título";

        /// <summary>
        /// Extensões de arquivo permitidas
        /// </summary>
        public static readonly string[] AllowedExtensions = { ".mp4", ".avi" };

        /// <summary>
        /// Tamanho máximo do arquivo em bytes (100MB)
        /// </summary>
        public const long MaxFileSizeBytes = 100 * 1024 * 1024;

        /// <summary>
        /// Tipos MIME suportados
        /// </summary>
        public static readonly string[] AllowedMimeTypes =
        {
            "video/mp4",
            "video/avi"
        };
    }
}