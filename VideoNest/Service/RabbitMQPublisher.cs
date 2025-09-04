using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using System;

namespace VideoNest.Service {
    public class RabbitMQPublisher {
        private readonly string _hostName;
        private readonly string _userName;
        private readonly string _password;
        private readonly string _queueName;
        private readonly IConnection _connection;
        private readonly IModel _channel;

        public RabbitMQPublisher(IConfiguration configuration) {
            _hostName = configuration["RabbitMQ:HostName"] ?? "rabbitmq";
            _userName = configuration["RabbitMQ:UserName"] ?? "admin";
            _password = configuration["RabbitMQ:Password"] ?? "admin";
            _queueName = configuration["RabbitMQ:QueueName"] ?? "video_queue";

            var factory = new ConnectionFactory {
                HostName = _hostName,
                UserName = _userName,
                Password = _password,
                Port = int.Parse(configuration["RabbitMQ:Port"] ?? "5672"),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(30)
            };

            _connection = factory.CreateConnection();
            _channel = _connection.CreateModel();
            _channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
        }

        public void PublishMessage(string message) {
            var body = System.Text.Encoding.UTF8.GetBytes(message);
            _channel.BasicPublish(exchange: "", routingKey: _queueName, basicProperties: null, body: body);
        }

        public void Dispose() {
            _channel?.Close();
            _connection?.Close();
        }
    }
}