using RabbitMQ.Client;
using System;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace VideoNest.Services;

public class RabbitMQPublisher : IRabbitMQPublisher, IDisposable {
    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _hostName;
    private readonly int _port;
    private readonly string _userName;
    private readonly string _password;
    private readonly string _queueName;
    private readonly string _exchangeName;
    private readonly string _routingKey;
    private readonly string _deadLetterExchange;
    private readonly string _deadLetterQueue;

    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed = false;

    public RabbitMQPublisher(ILogger<RabbitMQPublisher> logger, IConfiguration configuration) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        _hostName = _configuration["RabbitMQ:HostName"] ?? "rabbitmq_hackathon";
        _port = int.Parse(_configuration["RabbitMQ:Port"] ?? "5672");
        _userName = _configuration["RabbitMQ:UserName"] ?? "admin";
        _password = _configuration["RabbitMQ:Password"] ?? "admin";
        _queueName = _configuration["RabbitMQ:QueueName"] ?? "video_queue";
        _exchangeName = "video_exchange";
        _routingKey = "video_key";
        _deadLetterExchange = _configuration["RabbitMQ:DeadLetterExchange"] ?? "dlx_video_exchange";
        _deadLetterQueue = _configuration["RabbitMQ:DeadLetterQueue"] ?? "dlq_video_queue";
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
                _channel.ExchangeDeclare(_deadLetterExchange, "direct", durable: true, autoDelete: false);
                _logger.LogDebug("✅ Dead Letter Exchange: {Exchange}", _deadLetterExchange);

                // 2. Dead Letter Queue
                _channel.QueueDeclare(_deadLetterQueue, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueBind(_deadLetterQueue, _deadLetterExchange, _deadLetterQueue);
                _logger.LogDebug("✅ Dead Letter Queue: {Queue}", _deadLetterQueue);

                // 3. Main Exchange
                _channel.ExchangeDeclare(_exchangeName, "direct", durable: true, autoDelete: false);
                _logger.LogDebug("✅ Main Exchange: {Exchange}", _exchangeName);

                // 4. Main Queue com DLX arguments
                var queueArgs = new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", _deadLetterExchange },
                    { "x-dead-letter-routing-key", _deadLetterQueue },
                    { "x-message-ttl", 300000 }, // 5 minutos
                    { "x-max-length", 10000 }, // Limite de mensagens
                    { "x-overflow", "drop-head" } // Drop mais antigas se lotar
                };

                _channel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false, arguments: queueArgs);
                _channel.QueueBind(_queueName, _exchangeName, _routingKey);
                _logger.LogDebug("✅ Main Queue: {Queue} com DLX configurado", _queueName);

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
                if (message != null) {
                    try {
                        var messageDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                        if (messageDict?.TryGetValue("VideoId", out var videoIdObj) == true) {
                            videoId = videoIdObj?.ToString() ?? "unknown";
                        }
                    } catch {
                        videoId = "unknown";
                    }
                }

                properties.MessageId = $"video-{videoId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
                properties.Headers = new Dictionary<string, object>
                {
                    { "source", "VideoNest" },
                    { "timestamp", DateTime.UtcNow.ToString("O") },
                    { "retry-count", 0 }
                };

                _channel.BasicPublish(_exchangeName, _routingKey, properties, body);

                _logger.LogInformation("📤 Mensagem publicada: Exchange={Exchange}, RoutingKey={Key}, VideoId={VideoId}, Size={Size}B",
                    _exchangeName, _routingKey, videoId, body.Length);
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

            _channel.BasicPublish(_exchangeName, _routingKey, properties, body);

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
            var factory = new ConnectionFactory {
                HostName = _hostName,
                Port = _port,
                UserName = _userName,
                Password = _password,
                AutomaticRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromSeconds(60),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _connection?.Dispose();
            _connection = factory.CreateConnection();
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