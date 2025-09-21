using Microsoft.AspNetCore.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using VideoNest.DTO;
using VideoNest.Models;

namespace VideoNest.Services {
    /// <summary>
    /// Interface para o serviço de gerenciamento de vídeos
    /// Upload, consulta, atualizações e notificações
    /// </summary>
    public interface IVideoService {
        /// <summary>
        /// Upload de vídeo com validação, persistência e publicação na fila
        /// </summary>
        /// <param name="file">Arquivo de vídeo (.mp4/.avi)</param>
        /// <param name="request">Metadados do vídeo</param>
        /// <returns>ID do vídeo processado</returns>
        Task<int> UploadVideoAsync(IFormFile file, VideoUploadRequest request);

        /// <summary>
        /// Endpoint de teste para fila RabbitMQ
        /// </summary>
        /// <param name="message">Mensagem de teste</param>
        void PublishTestMessage(string message);

        /// <summary>
        /// Consulta de vídeo por ID com cache Redis
        /// </summary>
        /// <param name="id">ID do vídeo</param>
        /// <returns>Informações do vídeo ou null se não encontrado</returns>
        Task<VideoResult?> BuscaVideoPorIDService(int id);

        /// <summary>
        /// Atualizar status do vídeo (chamado pelo ScanForge)
        /// </summary>
        /// <param name="videoId">ID do vídeo</param>
        /// <param name="status">Novo status (Na Fila, Processando, Concluído, Erro)</param>
        /// <param name="errorMessage">Mensagem de erro se status for "Erro"</param>
        /// <param name="duration">Duração do vídeo em segundos</param>
        /// <returns>Task</returns>
        Task UpdateVideoStatusAsync(int videoId, string status, string? errorMessage = null, int duration = 0);

        /// <summary>
        /// Adicionar resultados de QR Codes (chamado pelo ScanForge)
        /// </summary>
        /// <param name="videoId">ID do vídeo</param>
        /// <param name="qrCodes">Lista de QR Codes detectados</param>
        /// <returns>Task</returns>
        Task AddQRCodesToVideoAsync(int videoId, List<QRCodeResult> qrCodes);

        /// <summary>
        /// Notificação SignalR de conclusão de processamento
        /// </summary>
        /// <param name="videoId">ID do vídeo</param>
        /// <param name="qrCodes">QR Codes detectados (opcional)</param>
        /// <returns>Task</returns>
        Task NotifyVideoCompletedAsync(int videoId, List<QRCodeResult>? qrCodes = null);
    }

    /// <summary>
    /// Interface para o publisher RabbitMQ - CORRIGIDA
    /// Agora suporta os métodos necessários para infraestrutura e publicação
    /// </summary>
    public interface IRabbitMQPublisher {
        /// <summary>
        /// Publica mensagem de vídeo para RabbitMQ (método principal)
        /// </summary>
        /// <param name="message">Objeto da mensagem (serializado para JSON)</param>
        Task PublishVideoMessageAsync(object message);

        /// <summary>
        /// Método de teste legado (mantido para compatibilidade)
        /// </summary>
        /// <param name="message">Mensagem de teste simples</param>
        void PublishMessage(string message);

        /// <summary>
        /// Declara infraestrutura RabbitMQ (exchanges, queues, bindings)
        /// </summary>
        Task DeclareInfrastructureAsync();
    }
}