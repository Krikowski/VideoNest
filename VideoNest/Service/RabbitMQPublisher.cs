using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace VideoNest.Services;

/// <summary>
/// Publicador responsável por declarar a infraestrutura RabbitMQ e publicar mensagens da aplicação.
/// </summary>
public sealed class RabbitMQPublisher : IRabbitMQPublisher, IDisposable
{
    private const int DefaultRabbitMqPort = 5672;
    private const string DefaultHostName = "localhost";
    private const string DefaultUserName = "admin";
    private const string DefaultPassword = "admin";
    private const string DefaultQueueName = "video_queue";
    private const string DefaultExchangeName = "video_exchange";
    private const string DefaultRoutingKey = "video_key";
    private const string DefaultDeadLetterExchange = "dlx_video_exchange";
    private const string DefaultDeadLetterQueue = "dlq_video_queue";
    private const string DefaultVirtualHost = "/";

    private readonly ILogger<RabbitMQPublisher> _logger;
    private readonly string _hostName;
    private readonly int _port;
    private readonly string _userName;
    private readonly string _password;
    private readonly string _virtualHost;
    private readonly string _queueName;
    private readonly string _exchangeName;
    private readonly string _routingKey;
    private readonly string _deadLetterExchange;
    private readonly string _deadLetterQueue;

    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;

    /// <summary>
    /// Inicializa uma nova instância de <see cref="RabbitMQPublisher"/>.
    /// </summary>
    /// <param name="logger">Logger da classe.</param>
    /// <param name="configuration">Configurações da aplicação.</param>
    /// <exception cref="ArgumentNullException">Lançada quando uma dependência obrigatória não é informada.</exception>
    public RabbitMQPublisher(ILogger<RabbitMQPublisher> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (configuration is null)
            throw new ArgumentNullException(nameof(configuration));

        _hostName = GetConfigurationValue(configuration, "RabbitMQ:HostName", DefaultHostName);
        _port = GetConfigurationIntValue(configuration, "RabbitMQ:Port", DefaultRabbitMqPort);
        _userName = GetConfigurationValue(configuration, "RabbitMQ:UserName", DefaultUserName);
        _password = GetConfigurationValue(configuration, "RabbitMQ:Password", DefaultPassword);
        _virtualHost = GetConfigurationValue(configuration, "RabbitMQ:VirtualHost", DefaultVirtualHost);
        _queueName = GetConfigurationValue(configuration, "RabbitMQ:QueueName", DefaultQueueName);
        _exchangeName = GetConfigurationValue(configuration, "RabbitMQ:ExchangeName", DefaultExchangeName);
        _routingKey = GetConfigurationValue(configuration, "RabbitMQ:RoutingKey", DefaultRoutingKey);
        _deadLetterExchange = GetConfigurationValue(configuration, "RabbitMQ:DeadLetterExchange", DefaultDeadLetterExchange);
        _deadLetterQueue = GetConfigurationValue(configuration, "RabbitMQ:DeadLetterQueue", DefaultDeadLetterQueue);
    }

    /// <summary>
    /// Declara exchanges, filas e bindings necessários para o processamento assíncrono de vídeos.
    /// </summary>
    public async Task DeclareInfrastructureAsync()
    {
        try
        {
            await Task.Run(() => {
                CreateConnection();

                if (_channel is null)
                {
                    _logger.LogWarning("Canal RabbitMQ não disponível para declaração de infraestrutura.");
                    return;
                }

                _logger.LogInformation("Declarando infraestrutura RabbitMQ.");

                _channel.ExchangeDeclare(_deadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false);
                _logger.LogDebug("Dead Letter Exchange declarada: {Exchange}", _deadLetterExchange);

                _channel.QueueDeclare(_deadLetterQueue, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueBind(_deadLetterQueue, _deadLetterExchange, _deadLetterQueue);
                _logger.LogDebug("Dead Letter Queue declarada: {Queue}", _deadLetterQueue);

                _channel.ExchangeDeclare(_exchangeName, ExchangeType.Direct, durable: true, autoDelete: false);
                _logger.LogDebug("Exchange principal declarada: {Exchange}", _exchangeName);

                var queueArgs = new Dictionary<string, object>
                {
                    { "x-dead-letter-exchange", _deadLetterExchange },
                    { "x-dead-letter-routing-key", _deadLetterQueue },
                    { "x-message-ttl", 300000 },
                    { "x-max-length", 10000 },
                    { "x-overflow", "drop-head" }
                };

                _channel.QueueDeclare(
                    _queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: queueArgs
                );

                _channel.QueueBind(_queueName, _exchangeName, _routingKey);

                _logger.LogInformation(
                    "Infraestrutura RabbitMQ declarada. Exchange={Exchange}; Queue={Queue}; RoutingKey={RoutingKey}",
                    _exchangeName,
                    _queueName,
                    _routingKey
                );
            });
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao declarar infraestrutura RabbitMQ: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Publica uma mensagem de vídeo em formato JSON no RabbitMQ.
    /// </summary>
    /// <param name="message">Objeto que será serializado e publicado.</param>
    public async Task PublishVideoMessageAsync(object message)
    {
        try
        {
            await Task.Run(() => {
                CreateConnection();

                if (_channel is null)
                {
                    _logger.LogWarning("Canal RabbitMQ não disponível. Mensagem de vídeo não enviada.");
                    return;
                }

                var json = JsonSerializer.Serialize(message);
                var body = Encoding.UTF8.GetBytes(json);

                var properties = CreateBasicProperties("application/json");
                var videoId = ExtractVideoId(json);

                properties.MessageId = $"video-{videoId}-{DateTime.UtcNow:yyyyMMddHHmmss}";
                properties.Headers = new Dictionary<string, object>
                {
                    { "source", "VideoNest" },
                    { "timestamp", DateTime.UtcNow.ToString("O") },
                    { "retry-count", 0 }
                };

                _channel.BasicPublish(_exchangeName, _routingKey, properties, body);

                _logger.LogInformation(
                    "Mensagem publicada. Exchange={Exchange}; RoutingKey={RoutingKey}; VideoId={VideoId}; Size={Size}B",
                    _exchangeName,
                    _routingKey,
                    videoId,
                    body.Length
                );
            });
        } catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao publicar mensagem: {MessageType}; Error={Error}",
                message?.GetType().Name ?? "Unknown",
                ex.Message
            );

            throw;
        }
    }

    /// <summary>
    /// Publica uma mensagem textual de teste no RabbitMQ.
    /// </summary>
    /// <param name="message">Mensagem textual que será publicada.</param>
    /// <exception cref="ArgumentException">Lançada quando a mensagem é vazia.</exception>
    public void PublishMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Mensagem obrigatória.", nameof(message));

        try
        {
            CreateConnection();

            if (_channel is null)
            {
                _logger.LogWarning("Canal RabbitMQ não disponível. Mensagem de teste não enviada.");
                return;
            }

            var body = Encoding.UTF8.GetBytes(message);
            var properties = CreateBasicProperties("text/plain");

            properties.MessageId = $"test-{DateTime.UtcNow:yyyyMMddHHmmss}";
            properties.Headers = new Dictionary<string, object>
            {
                { "source", "VideoNest-Test" },
                { "timestamp", DateTime.UtcNow.ToString("O") }
            };

            _channel.BasicPublish(_exchangeName, _routingKey, properties, body);

            _logger.LogInformation("Mensagem de teste publicada: {Message}", message);
        } catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao publicar mensagem de teste: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Libera recursos associados à conexão RabbitMQ.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        CleanupConnection();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    private void CreateConnection()
    {
        if (_connection is { IsOpen: true } && _channel is { IsOpen: true })
            return;

        try
        {
            var factory = new ConnectionFactory {
                HostName = _hostName,
                Port = _port,
                UserName = _userName,
                Password = _password,
                VirtualHost = _virtualHost,
                AutomaticRecoveryEnabled = true,
                RequestedHeartbeat = TimeSpan.FromSeconds(60),
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _channel?.Dispose();
            _connection?.Dispose();

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();

            _logger.LogDebug(
                "Conexão RabbitMQ criada. Host={Host}; Port={Port}; User={User}; VirtualHost={VirtualHost}",
                _hostName,
                _port,
                _userName,
                _virtualHost
            );
        } catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Falha ao criar conexão RabbitMQ. Host={Host}; Port={Port}; User={User}; VirtualHost={VirtualHost}; Error={Error}",
                _hostName,
                _port,
                _userName,
                _virtualHost,
                ex.Message
            );

            throw;
        }
    }

    private IBasicProperties CreateBasicProperties(string contentType)
    {
        if (_channel is null)
            throw new InvalidOperationException("Canal RabbitMQ não inicializado.");

        var properties = _channel.CreateBasicProperties();

        properties.Persistent = true;
        properties.ContentType = contentType;
        properties.DeliveryMode = 2;

        return properties;
    }

    private static string ExtractVideoId(string json)
    {
        try
        {
            var messageDict = JsonSerializer.Deserialize<Dictionary<string, object>>(json);

            return messageDict is not null &&
                   messageDict.TryGetValue("VideoId", out var videoIdObj) &&
                   videoIdObj is not null
                ? videoIdObj.ToString() ?? "unknown"
                : "unknown";
        } catch
        {
            return "unknown";
        }
    }

    private void CleanupConnection()
    {
        try
        {
            if (_channel is { IsOpen: true })
                _channel.Close(200, "Cleanup");

            if (_connection is { IsOpen: true })
                _connection.Close(200, "Cleanup");

            _channel?.Dispose();
            _connection?.Dispose();

            _logger.LogDebug("Conexão RabbitMQ limpa.");
        } catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erro no cleanup RabbitMQ: {Error}", ex.Message);
        } finally
        {
            _channel = null;
            _connection = null;
        }
    }

    private static string GetConfigurationValue(IConfiguration configuration, string key, string defaultValue)
    {
        var value = configuration[key];

        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value;
    }

    private static int GetConfigurationIntValue(IConfiguration configuration, string key, int defaultValue)
    {
        var value = configuration[key];

        return int.TryParse(value, out var parsedValue)
            ? parsedValue
            : defaultValue;
    }
}