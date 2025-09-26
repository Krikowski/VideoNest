using RabbitMQ.Client;
using System;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VideoNest.Services {
    public class RabbitMQPublisher : IRabbitMQPublisher, IDisposable {
        private readonly ILogger<RabbitMQPublisher> _logger;
        private readonly IConfiguration _configuration;
        private readonly IConnectionFactory _connectionFactory;
        private IConnection? _connection;
        private IModel? _channel;
        private bool _disposed = false;

        public RabbitMQPublisher(IConnectionFactory connectionFactory, IConfiguration configuration, ILogger<RabbitMQPublisher> logger) {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Declara toda a infraestrutura RabbitMQ
        /// </summary>
        public async Task DeclareInfrastructureAsync() {
            try {
                await Task.Run(() => {
                    CreateConnection();
                    if (_channel == null) {
                        _logger.LogWarning("⚠️ Canal RabbitMQ não disponível para declaração de infraestrutura");
                        return;
                    }
                    _logger.LogInformation("🔄 Declarando infraestrutura RabbitMQ...");
                    // 1. Dead Letter Exchange
                    _channel.ExchangeDeclare(_configuration["RabbitMQ:DeadLetterExchange"] ?? "dlx_video_exchange", "direct", durable: true, autoDelete: false);
                    _logger.LogDebug("✅ Dead Letter Exchange: {Exchange}", _configuration["RabbitMQ:DeadLetterExchange"] ?? "dlx_video_exchange");
                    // 2. Dead Letter Queue
                    _channel.QueueDeclare(_configuration["RabbitMQ:DeadLetterQueue"] ?? "dlq_video_queue", durable: true, exclusive: false, autoDelete: false);
                    _channel.QueueBind(_configuration["RabbitMQ:DeadLetterQueue"] ?? "dlq_video_queue", _configuration["RabbitMQ:DeadLetterExchange"] ?? "dlx_video_exchange", _configuration["RabbitMQ:DeadLetterQueue"] ?? "dlq_video_queue");
                    _logger.LogDebug("✅ Dead Letter Queue: {Queue}", _configuration["RabbitMQ:DeadLetterQueue"] ?? "dlq_video_queue");
                    // 3. Main Exchange
                    _channel.ExchangeDeclare("video_exchange", "direct", durable: true, autoDelete: false);
                    _logger.LogDebug("✅ Main Exchange: {Exchange}", "video_exchange");
                    // 4. Main Queue com DLX arguments
                    var queueArgs = new Dictionary<string, object>
                    {
                        { "x-dead-letter-exchange", _configuration["RabbitMQ:DeadLetterExchange"] ?? "dlx_video_exchange" },
                        { "x-dead-letter-routing-key", _configuration["RabbitMQ:DeadLetterQueue"] ?? "dlq_video_queue" },
                        { "x-message-ttl", 300000 }, // 5 minutos
                        { "x-max-length", 10000 }, // Limite de mensagens
                        { "x-overflow", "drop-head" } // Drop mais antigas se lotar
                    };
                    _channel.QueueDeclare(_configuration["RabbitMQ:QueueName"] ?? "video_queue", durable: true, exclusive: false, autoDelete: false, arguments: queueArgs);
                    _channel.QueueBind(_configuration["RabbitMQ:QueueName"] ?? "video_queue", "video_exchange", "video_key");
                    _logger.LogDebug("✅ Main Queue: {Queue} com DLX configurado", _configuration["RabbitMQ:QueueName"] ?? "video_queue");
                    _logger.LogInformation("✅ Infraestrutura RabbitMQ declarada completamente");
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "❌ Falha ao declarar infraestrutura RabbitMQ: {Error}", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Publica mensagem de vídeo para RabbitMQ (método principal)
        /// </summary>
        public async Task PublishVideoMessageAsync(object message) {
            if (message == null) {
                _logger.LogWarning("⚠️ Mensagem nula - publicação ignorada");
                return;
            }
            try {
                await Task.Run(() => {
                    CreateConnection();
                    if (_channel == null) {
                        _logger.LogWarning("⚠️ Canal RabbitMQ não disponível - mensagem não enviada");
                        return;
                    }
                    var json = JsonSerializer.Serialize(message);
                    var body = Encoding.UTF8.GetBytes(json);
                    var properties = _channel.CreateBasicProperties();
                    properties.Persistent = true;
                    properties.ContentType = "application/json";
                    properties.DeliveryMode = 2; // Persistent
                    // Extrair VideoId de forma segura
                    string videoId = "unknown";
                    try {
                        var messageDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (messageDict?.TryGetValue("VideoId", out var videoIdObj) == true) {
                            videoId = videoIdObj?.ToString() ?? "unknown";
                        }
                    } catch {
                        videoId = "unknown";
                    }
                    properties.MessageId = $"video-{videoId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
                    properties.Headers = new Dictionary<string, object>
                    {
                        { "source", "VideoNest" },
                        { "timestamp", DateTime.UtcNow.ToString("O") },
                        { "retry-count", 0 }
                    };
                    _channel.BasicPublish("video_exchange", "video_key", properties, body);
                    _logger.LogInformation("📤 Mensagem publicada: Exchange={Exchange}, RoutingKey={Key}, VideoId={VideoId}, Size={Size}B",
                        "video_exchange", "video_key", videoId, body.Length);
                });
            } catch (Exception ex) {
                _logger.LogError(ex, "❌ Falha ao publicar mensagem: {MessageType}, Error={Error}",
                    message?.GetType().Name ?? "Unknown", ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Método de teste legado (mantido para compatibilidade com IVideoService)
        /// </summary>
        public void PublishMessage(string message) {
            if (string.IsNullOrEmpty(message)) {
                _logger.LogWarning("⚠️ Mensagem de teste vazia - publicação ignorada");
                return;
            }
            try {
                CreateConnection();
                if (_channel == null) {
                    _logger.LogWarning("⚠️ Canal RabbitMQ não disponível - mensagem de teste não enviada");
                    return;
                }
                var body = Encoding.UTF8.GetBytes(message);
                var properties = _channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.ContentType = "text/plain";
                properties.DeliveryMode = 2;
                properties.MessageId = $"test-{DateTime.UtcNow:yyyyMMddHHmmss}";
                properties.Headers = new Dictionary<string, object>
                {
                    { "source", "VideoNest-Test" },
                    { "timestamp", DateTime.UtcNow.ToString("O") }
                };
                _channel.BasicPublish("video_exchange", "video_key", properties, body);
                _logger.LogInformation("🧪 Mensagem de teste publicada: {Message}", message);
            } catch (Exception ex) {
                _logger.LogError(ex, "❌ Falha ao publicar mensagem de teste: {Error}", ex.Message);
                throw;
            }
        }

        private void CreateConnection() {
            if (_connection != null && _connection.IsOpen && _channel != null && _channel.IsOpen)
                return;
            try {
                _connection?.Dispose();
                _connection = _connectionFactory.CreateConnection();
                _channel?.Dispose();
                _channel = _connection.CreateModel();
                _logger.LogDebug("🔗 Conexão RabbitMQ criada/reativada");
            } catch (Exception ex) {
                _logger.LogError(ex, "❌ Falha ao criar conexão RabbitMQ: {Error}", ex.Message);
                throw;
            }
        }

        private void CleanupConnection() {
            try {
                _channel?.Close(200, "Cleanup");
                _connection?.Close(200, "Cleanup");
                _logger.LogDebug("🧹 Conexão RabbitMQ limpa");
            } catch (Exception ex) {
                _logger.LogWarning(ex, "⚠️ Erro no cleanup RabbitMQ: {Error}", ex.Message);
            }
        }

        public void Dispose() {
            if (!_disposed) {
                CleanupConnection();
                _disposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }
}