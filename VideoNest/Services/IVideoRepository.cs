using VideoNest.Models;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VideoNest.Repositories {
    /// <summary>
    /// Interface para repositório de vídeos (MongoDB).
    /// </summary>
    public interface IVideoRepository {
        /// <summary>
        /// Gera próximo ID sequencial (Counter Pattern).
        /// </summary>
        /// <returns>ID incremental.</returns>
        Task<int> GetNextIdAsync();

        /// <summary>
        /// Salva vídeo inicial ("Na Fila").
        /// </summary>
        /// <param name="video">Entidade vídeo.</param>
        Task SaveVideoAsync(VideoResult video);

        /// <summary>
        /// Recupera vídeo por ID (status + QR).
        /// </summary>
        /// <param name="id">ID vídeo.</param>
        /// <returns>VideoResult ou null.</returns>
        Task<VideoResult?> GetVideoByIdAsync(int id);

        /// <summary>
        /// Atualiza status/duração/erro.
        /// </summary>
        /// <param name="videoId">ID vídeo.</param>
        /// <param name="status">Novo status.</param>
        /// <param name="errorMessage">Erro opcional.</param>
        /// <param name="duration">Duração seg. (default 0).</param>
        Task UpdateStatusAsync(int videoId, string status, string? errorMessage = null, int duration = 0);

        /// <summary>
        /// Adiciona QR Codes ao vídeo.
        /// </summary>
        /// <param name="videoId">ID vídeo.</param>
        /// <param name="qrs">Lista QRCodeResult.</param>
        Task AddQRCodesAsync(int videoId, List<QRCodeResult> qrs);
    }
}