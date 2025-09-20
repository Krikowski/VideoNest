using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;

namespace VideoNest.Models {
    /// <summary>
    /// Representa um vídeo e seus resultados de processamento
    /// </summary>
    public class VideoResult {
        /// <summary>
        /// ID único do vídeo (Primary Key)
        /// </summary>
        /// <remarks>
        /// Gerado sequencialmente via VideoCounter.
        /// [BsonId] mapeia para _id do MongoDB.
        /// </remarks>
        [BsonId]
        [BsonRepresentation(BsonType.Int32)]
        [Required]
        [Range(1, int.MaxValue)]
        public int VideoId { get; set; }

        /// <summary>
        /// Título do vídeo
        /// </summary>
        /// <remarks>
        /// Do VideoUploadRequest.Title, default: "Vídeo sem título".
        /// </remarks>
        [Required]
        [StringLength(100)]
        public string Title { get; set; } = "Vídeo sem título";

        /// <summary>
        /// Descrição do vídeo (opcional)
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Caminho do arquivo no disco
        /// </summary>
        /// <remarks>
        /// Ex: "/uploads/video-123.mp4" - gerado no upload.
        /// </remarks>
        [Required]
        public string? FilePath { get; set; }

        /// <summary>
        /// Status atual do processamento
        /// </summary>
        /// <remarks>
        /// Estados: "Na Fila", "Processando", "Concluído", "Erro".
        /// Default: "Na Fila" após upload (RF6).
        /// </remarks>
        [Required]
        public string Status { get; set; } = "Na Fila";

        /// <summary>
        /// Data de criação (UTC)
        /// </summary>
        public DateTime? CreatedAt { get; set; }

        /// <summary>
        /// Duração do vídeo em segundos
        /// </summary>
        /// <remarks>
        /// Extraído via FFmpeg (ffprobe).
        /// </remarks>
        [Range(0, int.MaxValue)]
        public int Duration { get; set; }

        /// <summary>
        /// Mensagem de erro (se Status = "Erro")
        /// </summary>
        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// QR Codes detectados (RF7)
        /// </summary>
        /// <remarks>
        /// Array preenchido pelo ScanForge após ZXing.Net.
        /// Cada item: { Content, Timestamp }.
        /// </remarks>
        public List<QRCodeResult> QRCodes { get; set; } = new();

        /// <summary>
        /// Valida o documento antes de persistir
        /// </summary>
        /// <returns>True se válido</returns>
        public bool IsValid(out string? error) {
            if (VideoId <= 0) { error = "VideoId inválido"; return false; }
            if (string.IsNullOrEmpty(Title)) { error = "Título obrigatório"; return false; }
            if (string.IsNullOrEmpty(FilePath)) { error = "FilePath obrigatório"; return false; }

            var validStatuses = new[] { "Na Fila", "Processando", "Concluído", "Erro" };
            if (!validStatuses.Contains(Status)) {
                error = $"Status inválido: {Status}";
                return false;
            }

            if (Status == "Concluído" && QRCodes.Count > 1000) {
                error = "Máximo 1000 QR Codes";
                return false;
            }

            error = null;
            return true;
        }
    }

    /// <summary>
    /// QR Code detectado em um frame do vídeo
    /// </summary>
    public class QRCodeResult {
        /// <summary>
        /// Conteúdo decodificado do QR Code
        /// </summary>
        /// <remarks>
        /// Ex: URL, email, texto. Máximo 500 caracteres.
        /// </remarks>
        [StringLength(500)]
        public string? Content { get; set; }

        /// <summary>
        /// Timestamp do frame (segundos)
        /// </summary>
        /// <remarks>
        /// Calculado: frame_index / fps. Ex: frame 30 @ 2fps = 15s.
        /// </remarks>
        [Range(0, int.MaxValue)]
        public int Timestamp { get; set; }

        /// <summary>
        /// Valida o QR Code
        /// </summary>
        public bool IsValid() => !string.IsNullOrWhiteSpace(Content) && Timestamp >= 0;
    }

    /// <summary>
    /// Contador sequencial para IDs de vídeo
    /// </summary>
    /// <remarks>
    /// FASE 01: Gera VideoId incremental (1, 2, 3...) via MongoDB $inc.
    /// Documento fixo: { _id: "video_counter", sequence: 123 }
    /// </remarks>
    public class VideoCounter {
        /// <summary>
        /// ID fixo do contador
        /// </summary>
        [BsonId]
        public string Id { get; set; } = "video_counter";

        /// <summary>
        /// Valor sequencial atual
        /// </summary>
        public int Sequence { get; set; }
    }

    /// <summary>
    /// Constantes de domínio para vídeos
    /// </summary>
    public static class VideoConstants {
        /// <summary>
        /// Status válidos do processamento
        /// </summary>
        public static readonly string[] ValidStatuses =
        { "Na Fila", "Processando", "Concluído", "Erro" };

        /// <summary>
        /// Extensões suportadas
        /// </summary>
        public static readonly string[] SupportedExtensions = { ".mp4", ".avi" };

        /// <summary>
        /// Tamanho máximo (100MB)
        /// </summary>
        public const long MaxFileSizeBytes = 100 * 1024 * 1024;
    }
}