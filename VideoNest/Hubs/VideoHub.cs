using Microsoft.AspNetCore.SignalR;
using VideoNest.Models;

namespace VideoNest.Hubs;

/// <summary>
/// SignalR Hub para notificações em tempo real (bônus)
/// Notifica clientes sobre progresso e conclusão de processamento
/// Endpoints: /videoHub
/// </summary>
public class VideoHub : Hub {
    private readonly ILogger<VideoHub> _logger;

    public VideoHub(ILogger<VideoHub> logger) {
        _logger = logger;
    }

    /// <summary>
    /// Notifica conclusão de processamento (chamado pelo ScanForge)
    /// RF7 + Bônus: Resultados em tempo real
    /// </summary>
    /// <param name="video">VideoResult completo com QRs e status</param>
    public async Task VideoProcessed(VideoResult video) {
        try {
            await Clients.All.SendAsync("VideoProcessed", video);
            _logger.LogInformation("🔔 SignalR: VideoId={VideoId} notificado - Status: {Status}, QRs: {QrCount}",
                video.VideoId, video.Status, video.QRCodes?.Count ?? 0);
        } catch (Exception ex) {
            _logger.LogError(ex, "❌ Erro ao notificar VideoProcessed para VideoId={VideoId}", video.VideoId);
        }
    }

    /// <summary>
    /// Notifica progresso do processamento
    /// RF6 + Bônus: Status em tempo real
    /// </summary>
    /// <param name="videoId">ID do vídeo</param>
    /// <param name="status">Status atual</param>
    /// <param name="progress">Progresso percentual (0-100)</param>
    public async Task UpdateProgress(int videoId, string status, int progress = 0) {
        try {
            await Clients.All.SendAsync("VideoProgress", videoId, status, progress);
            _logger.LogDebug("📊 SignalR: VideoId={VideoId} → {Status} ({Progress}%)", videoId, status, progress);
        } catch (Exception ex) {
            _logger.LogWarning(ex, "⚠️ Erro ao notificar progresso para VideoId={VideoId}", videoId);
        }
    }

    /// <summary>
    /// Cliente se inscreve para notificações de vídeo específico
    /// </summary>
    public async Task JoinVideoGroup(int videoId) {
        try {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"Video_{videoId}");
            _logger.LogInformation("👥 SignalR: Cliente {ConnectionId} inscreveu-se no grupo Video_{VideoId}",
                Context.ConnectionId, videoId);
        } catch (Exception ex) {
            _logger.LogError(ex, "❌ Erro ao inscrever cliente no grupo Video_{VideoId}", videoId);
        }
    }

    /// <summary>
    /// Conexão de cliente estabelecida
    /// </summary>
    public override async Task OnConnectedAsync() {
        _logger.LogInformation("🔗 SignalR: Cliente conectado - ConnectionId: {ConnectionId}, User: {User}",
            Context.ConnectionId, Context.User?.Identity?.Name ?? "Anonymous");
        await Clients.Caller.SendAsync("Connected", new { ConnectionId = Context.ConnectionId });
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Cliente desconectado
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception) {
        _logger.LogInformation("🔌 SignalR: Cliente desconectado - ConnectionId: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}