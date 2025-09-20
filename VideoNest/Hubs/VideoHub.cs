using Microsoft.AspNetCore.SignalR;
using VideoNest.Models;

namespace VideoNest.Hubs {
    /// <summary>
    /// Hub SignalR para notificações real-time de processamento de vídeos
    /// </summary>
    /// <remarks>
    /// FASE 04 - Bônus: Notificação em Tempo Real (Requisito Opcional)
    /// 
    /// <para><strong>Funcionalidade:</strong></para>
    /// <list type="bullet">
    /// <item>Notifica clientes quando o processamento de vídeo termina</item>
    /// <item>Envia status atualizado ("Processando", "Concluído", "Erro")</item>
    /// <item>Inclui lista de QR Codes detectados com timestamps</item>
    /// <item>Suporte a broadcast para todos os clientes conectados</item>
    /// </list>
    /// 
    /// <para><strong>Fluxo Técnico:</strong></para>
    /// <code>ScanForge → InvokeAsync("VideoProcessed") → Hub → Clients.All → Frontend</code>
    /// 
    /// <para><strong>Impacto na Demo:</strong> Atualizações instantâneas impressionam professores</para>
    /// <para><strong>Requisito Opcional:</strong> "Notificação em Tempo Real via SignalR"</para>
    /// </remarks>
    public class VideoHub : Hub {
        private readonly ILogger<VideoHub> _logger;

        /// <summary>
        /// Inicializa uma nova instância do VideoHub com injeção de dependências
        /// </summary>
        /// <param name="logger">Logger para auditoria de conexões e mensagens</param>
        public VideoHub(ILogger<VideoHub> logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Notifica todos os clientes conectados sobre o status de processamento de um vídeo
        /// </summary>
        /// <param name="videoId">Identificador único do vídeo processado</param>
        /// <param name="status">Status atual do processamento</param>
        /// <param name="qrs">Lista de QR Codes detectados (opcional)</param>
        /// <returns>Tarefa assíncrona de envio da notificação</returns>
        public async Task VideoProcessed(int videoId, string status, List<QRCodeResult>? qrs = null) {
            try {
                _logger.LogDebug("📡 VideoHub: Recebida notificação - VideoId={VideoId}, Status={Status}",
                    videoId, status);

                if (videoId <= 0) {
                    _logger.LogWarning("⚠️ VideoHub: VideoId inválido ({VideoId}) ignorado", videoId);
                    return;
                }

                if (string.IsNullOrWhiteSpace(status)) {
                    _logger.LogWarning("⚠️ VideoHub: Status vazio para VideoId={VideoId} ignorado", videoId);
                    return;
                }

                var qrCodes = qrs ?? new List<QRCodeResult>();
                var qrCount = qrCodes.Count;

                var notification = new VideoProcessingNotification {
                    VideoId = videoId,
                    Status = status,
                    Timestamp = DateTime.UtcNow,
                    QRCodes = qrCodes,
                    ErrorMessage = null, // Pode ser setado se status == "Erro"
                    ProgressPercentage = null
                };

                await Clients.All.SendAsync("VideoProcessed", notification);

                _logger.LogInformation("✅ VideoHub: Notificação enviada - VideoId={VideoId}, Status={Status}, QRCodes={Count}",
                    videoId, status, qrCount);
            } catch (Exception ex) {
                _logger.LogError(ex, "💥 VideoHub: Erro ao processar notificação VideoId={VideoId}, Status={Status}",
                    videoId, status);

                try {
                    await Clients.All.SendAsync("VideoProcessingError", new VideoProcessingErrorNotification {
                        VideoId = videoId,
                        Error = "Erro interno no sistema de notificações",
                        Timestamp = DateTime.UtcNow
                    });
                } catch (Exception notifyEx) {
                    _logger.LogWarning(notifyEx, "⚠️ VideoHub: Falha secundária ao notificar erro para VideoId={VideoId}", videoId);
                }
            }
        }

        /// <summary>
        /// Método auxiliar para clientes se conectarem a um grupo específico de vídeo
        /// </summary>
        /// <param name="videoId">ID do vídeo para monitoramento</param>
        /// <returns>Tarefa assíncrona de inscrição no grupo</returns>
        public async Task JoinVideoGroup(int videoId) {
            if (videoId <= 0) {
                _logger.LogWarning("⚠️ VideoHub: Tentativa de join com VideoId inválido ({VideoId})", videoId);
                return;
            }

            var groupName = $"video-{videoId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

            _logger.LogDebug("👥 VideoHub: Cliente {ConnectionId} entrou no grupo {GroupName}",
                Context.ConnectionId, groupName);
        }

        /// <summary>
        /// Método auxiliar para clientes saírem de um grupo específico de vídeo
        /// </summary>
        /// <param name="videoId">ID do vídeo para deixar de monitorar</param>
        /// <returns>Tarefa assíncrona de saída do grupo</returns>
        public async Task LeaveVideoGroup(int videoId) {
            if (videoId <= 0)
                return;

            var groupName = $"video-{videoId}";
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

            _logger.LogDebug("👋 VideoHub: Cliente {ConnectionId} saiu do grupo {GroupName}",
                Context.ConnectionId, groupName);
        }

        /// <summary>
        /// Sobrescreve o método de conexão para logging e métricas
        /// </summary>
        /// <returns>Tarefa assíncrona de conexão</returns>
        public override async Task OnConnectedAsync() {
            _logger.LogInformation("🔌 VideoHub: Novo cliente conectado - ConnectionId={ConnectionId}",
                Context.ConnectionId);

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Sobrescreve o método de desconexão para logging e limpeza
        /// </summary>
        /// <param name="exception">Exceção de desconexão (se houver)</param>
        /// <returns>Tarefa assíncrona de desconexão</returns>
        public override async Task OnDisconnectedAsync(Exception? exception) {
            _logger.LogInformation("🔌 VideoHub: Cliente desconectado - ConnectionId={ConnectionId}",
                Context.ConnectionId);

            if (exception != null) {
                _logger.LogWarning(exception, "⚠️ VideoHub: Desconexão com erro - ConnectionId={ConnectionId}",
                    Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }

    #region DTOs de Notificação

    /// <summary>
    /// Payload de notificação enviado para clientes
    /// </summary>
    /// <remarks>
    /// Serializado automaticamente como JSON para todos os clientes.
    /// Estrutura flexível para diferentes tipos de notificação.
    /// </remarks>
    public class VideoProcessingNotification {
        /// <summary>
        /// Identificador único do vídeo
        /// </summary>
        /// <example>123</example>
        public int VideoId { get; set; }

        /// <summary>
        /// Status atual do processamento
        /// </summary>
        /// <example>"Concluído"</example>
        public string Status { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp da notificação (UTC)
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Lista de QR Codes detectados
        /// </summary>
        public List<QRCodeResult> QRCodes { get; set; } = new();

        /// <summary>
        /// Quantidade de QR Codes encontrados (calculada)
        /// </summary>
        public int QRCodesCount => QRCodes.Count;

        /// <summary>
        /// Flag indicando se QR Codes foram encontrados (calculada)
        /// </summary>
        public bool HasQRCodes => QRCodes.Count > 0;

        /// <summary>
        /// Mensagem de erro (se Status = "Erro")
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Porcentagem de progresso estimada (opcional)
        /// </summary>
        public int? ProgressPercentage { get; set; }
    }

    /// <summary>
    /// Payload de erro de processamento enviado para clientes
    /// </summary>
    public class VideoProcessingErrorNotification {
        /// <summary>
        /// Identificador único do vídeo
        /// </summary>
        public int VideoId { get; set; }

        /// <summary>
        /// Mensagem de erro amigável
        /// </summary>
        /// <example>"Erro interno no sistema de notificações"</example>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp do erro (UTC)
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// Detalhes técnicos (para logging, não mostrado ao usuário)
        /// </summary>
        public string? TechnicalDetails { get; set; }
    }

    #endregion

    #region Constantes do Hub

    /// <summary>
    /// Nomes de eventos usados no SignalR
    /// </summary>
    public static class HubEventNames {
        /// <summary>
        /// Evento de processamento concluído
        /// </summary>
        public const string VideoProcessed = "VideoProcessed";

        /// <summary>
        /// Evento de erro no processamento
        /// </summary>
        public const string VideoProcessingError = "VideoProcessingError";

        /// <summary>
        /// Evento de progresso de processamento
        /// </summary>
        public const string VideoProcessingProgress = "VideoProcessingProgress";
    }

    /// <summary>
    /// Nomes de grupos para organização de clientes
    /// </summary>
    public static class HubGroupNames {
        /// <summary>
        /// Formato para grupos de vídeo específico
        /// </summary>
        /// <example>"video-123"</example>
        public const string VideoGroupFormat = "video-{0}";

        /// <summary>
        /// Grupo para administradores
        /// </summary>
        public const string AdminGroup = "admins";

        /// <summary>
        /// Grupo para notificações de sistema
        /// </summary>
        public const string SystemNotifications = "system";
    }

    #endregion

    #region Status de Processamento (Enum)

    /// <summary>
    /// Status possíveis do processamento de vídeo
    /// </summary>
    /// <remarks>
    /// Alinhado com os valores usados no MongoDB (VideoResult.Status).
    /// Usado para validação e documentação consistente.
    /// </remarks>
    public enum VideoProcessingStatus {
        /// <summary>
        /// Vídeo aguardando na fila RabbitMQ
        /// </summary>
        NaFila,

        /// <summary>
        /// ScanForge iniciou extração de frames
        /// </summary>
        Processando,

        /// <summary>
        /// Processamento concluído com sucesso
        /// </summary>
        Concluido,

        /// <summary>
        /// Falha no processamento (FFmpeg, ZXing, etc.)
        /// </summary>
        Erro
    }

    #endregion
}