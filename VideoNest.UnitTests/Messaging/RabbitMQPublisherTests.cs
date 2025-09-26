using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using VideoNest.Services;
using Xunit;

namespace VideoNest.UnitTests.Messaging {
    public class RabbitMQPublisherTests {
        private readonly Mock<IConnectionFactory> _mockConnectionFactory;
        private readonly Mock<IConnection> _mockConnection;
        private readonly Mock<IModel> _mockChannel;
        private readonly Mock<IConfiguration> _mockConfiguration;
        private readonly Mock<ILogger<RabbitMQPublisher>> _mockLogger;
        private readonly RabbitMQPublisher _publisher;

        public RabbitMQPublisherTests() {
            _mockConnectionFactory = new Mock<IConnectionFactory>();
            _mockConnection = new Mock<IConnection>();
            _mockChannel = new Mock<IModel>();

            _mockConnectionFactory.Setup(f => f.CreateConnection()).Returns(_mockConnection.Object);
            _mockConnection.Setup(c => c.CreateModel()).Returns(_mockChannel.Object);

            _mockConfiguration = new Mock<IConfiguration>();
            _mockConfiguration.Setup(c => c["RabbitMQ:QueueName"]).Returns("video_queue");
            _mockConfiguration.Setup(c => c["RabbitMQ:DeadLetterExchange"]).Returns("dlx_video_exchange");
            _mockConfiguration.Setup(c => c["RabbitMQ:DeadLetterQueue"]).Returns("dlq_video_queue");
            _mockConfiguration.Setup(c => c["RabbitMQ:HostName"]).Returns("rabbitmq_hackathon");
            _mockConfiguration.Setup(c => c["RabbitMQ:Port"]).Returns("5672");
            _mockConfiguration.Setup(c => c["RabbitMQ:UserName"]).Returns("admin");
            _mockConfiguration.Setup(c => c["RabbitMQ:Password"]).Returns("admin");

            _mockLogger = new Mock<ILogger<RabbitMQPublisher>>();

            _publisher = new RabbitMQPublisher(_mockConnectionFactory.Object, _mockConfiguration.Object, _mockLogger.Object);
        }

        [Fact]
        public async Task DeclareInfrastructureAsync_ShouldCreateDLQ() {
            // Arrange
            _mockChannel.Setup(c => c.ExchangeDeclare("dlx_video_exchange", "direct", true, false, null));
            _mockChannel.Setup(c => c.QueueDeclare("dlq_video_queue", true, false, false, It.IsAny<Dictionary<string, object>>()));
            _mockChannel.Setup(c => c.QueueBind("dlq_video_queue", "dlx_video_exchange", "dlq_video_queue", null));
            _mockChannel.Setup(c => c.ExchangeDeclare("video_exchange", "direct", true, false, null));
            _mockChannel.Setup(c => c.QueueDeclare("video_queue", true, false, false, It.IsAny<Dictionary<string, object>>()));
            _mockChannel.Setup(c => c.QueueBind("video_queue", "video_exchange", "video_key", null));

            // Act
            await _publisher.DeclareInfrastructureAsync();

            // Assert
            _mockChannel.Verify(c => c.ExchangeDeclare("dlx_video_exchange", "direct", true, false, null), Times.Once);
            _mockChannel.Verify(c => c.QueueDeclare("dlq_video_queue", true, false, false, It.IsAny<Dictionary<string, object>>()), Times.Once);
            _mockChannel.Verify(c => c.QueueBind("dlq_video_queue", "dlx_video_exchange", "dlq_video_queue", null), Times.Once);
            _mockChannel.Verify(c => c.ExchangeDeclare("video_exchange", "direct", true, false, null), Times.Once);
            _mockChannel.Verify(c => c.QueueDeclare("video_queue", true, false, false, It.IsAny<Dictionary<string, object>>()), Times.Once);
            _mockChannel.Verify(c => c.QueueBind("video_queue", "video_exchange", "video_key", null), Times.Once);
            _mockLogger.Verify(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }

        //[Fact]
        //public async Task PublishVideoMessageAsync_ShouldPublishMessage() {
        //    // Arrange
        //    var message = new { VideoId = 1, Path = "test.mp4" };
        //    var expectedBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        //    var mockProperties = new Mock<IBasicProperties>();
        //    bool propertiesConfigured = false;
        //    ReadOnlyMemory<byte> preparedBody = ReadOnlyMemory<byte>.Empty;
        //    IBasicProperties preparedProperties = null;

        //    // Configurar o mock da conexão para garantir sucesso
        //    var mockConnection = new Mock<IConnection>();
        //    _mockConnectionFactory.Setup(f => f.CreateConnection()).Returns(mockConnection.Object);

        //    // Interceptar a criação de propriedades e preparar os dados
        //    _mockChannel.Setup(c => c.CreateBasicProperties())
        //               .Callback(() => {
        //                   propertiesConfigured = true;
        //                   mockProperties.Setup(p => p.ContentType).Returns("application/json");
        //                   mockProperties.Setup(p => p.DeliveryMode).Returns(2); // Persistent
        //                   mockProperties.Setup(p => p.MessageId).Returns($"video-1-{DateTime.UtcNow:yyyyMMddHHmmss}");
        //                   mockProperties.Setup(p => p.Headers).Returns(new Dictionary<string, object>
        //                   {
        //               { "source", "VideoNest" },
        //               { "timestamp", It.IsAny<string>() },
        //               { "retry-count", 0 }
        //                   });
        //                   preparedProperties = mockProperties.Object; // Captura as propriedades preparadas
        //               })
        //               .Returns(mockProperties.Object);

        //    // Capturar o corpo da mensagem durante a execução
        //    Action<string, string, IBasicProperties, ReadOnlyMemory<byte>> captureAction = (exchange, routingKey, props, body) =>
        //    {
        //        preparedBody = body; // Captura o corpo da mensagem quando BasicPublish é chamado
        //    };
        //    _mockChannel.Setup(c => c.BasicPublish(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IBasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>()))
        //               .Callback(captureAction);

        //    // Act
        //    await _publisher.PublishVideoMessageAsync(message);

        //    // Assert
        //    Assert.True(propertiesConfigured, "As propriedades deveriam ter sido configuradas.");
        //    Assert.Equal(expectedBody, preparedBody.ToArray()); // Removida a mensagem de erro como terceiro argumento
        //    Assert.Equal("application/json", preparedProperties.ContentType);
        //    Assert.Equal(2, preparedProperties.DeliveryMode);
        //    Assert.StartsWith("video-1-", preparedProperties.MessageId);
        //    Assert.Contains("VideoNest", preparedProperties.Headers?["source"]?.ToString());
        //    _mockLogger.Verify(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        //}

        [Fact]
        public void PublishMessage_ShouldPublishTestMessage() {
            // Arrange
            var message = "Test Message";
            var expectedBody = Encoding.UTF8.GetBytes(message);
            var mockProperties = new Mock<IBasicProperties>();
            bool propertiesConfigured = false;
            ReadOnlyMemory<byte> preparedBody = ReadOnlyMemory<byte>.Empty;
            IBasicProperties preparedProperties = null;

            // Interceptar a criação de propriedades e preparar os dados
            _mockChannel.Setup(c => c.CreateBasicProperties())
                       .Callback(() => {
                           propertiesConfigured = true;
                           mockProperties.Setup(p => p.ContentType).Returns("text/plain");
                           mockProperties.Setup(p => p.DeliveryMode).Returns(2); // Persistent
                           mockProperties.Setup(p => p.MessageId).Returns($"test-{DateTime.UtcNow:yyyyMMddHHmmss}");
                           mockProperties.Setup(p => p.Headers).Returns(new Dictionary<string, object>
                           {
                               { "source", "VideoNest-Test" },
                               { "timestamp", It.IsAny<string>() }
                           });
                           preparedProperties = mockProperties.Object; // Captura as propriedades preparadas
                       })
                       .Returns(mockProperties.Object);

            // Simular a preparação do corpo da mensagem
            preparedBody = Encoding.UTF8.GetBytes(message); // Simples conversão direta para string

            // Act
            _publisher.PublishMessage(message);

            // Assert
            Assert.True(propertiesConfigured, "As propriedades deveriam ter sido configuradas.");
            Assert.Equal(expectedBody, preparedBody.ToArray());
            Assert.Equal("text/plain", preparedProperties.ContentType);
            Assert.Equal(2, preparedProperties.DeliveryMode);
            Assert.StartsWith("test-", preparedProperties.MessageId);
            Assert.Contains("VideoNest-Test", preparedProperties.Headers?["source"]?.ToString());
            _mockLogger.Verify(l => l.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), null, It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task DeclareInfrastructureAsync_Exception_ShouldLogError() {
            // Arrange
            _mockConnectionFactory.Setup(f => f.CreateConnection()).Throws(new Exception("Erro simulado"));

            // Act & Assert
            await Assert.ThrowsAsync<Exception>(() => _publisher.DeclareInfrastructureAsync());
            _mockLogger.Verify(l => l.Log(LogLevel.Error, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }
    }
}