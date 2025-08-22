using RabbitMQ.Client;
using System.Text;

namespace VideoNest.Service {
    public class Producer {
        public static void SendMessage(string message) {
            var factory = new ConnectionFactory {
                HostName = "rabbitmq",
                UserName = "admin",
                Password = "admin",
                Port = 5672,
                DispatchConsumersAsync = false // importante para simplificar
            };

            using IConnection connection = factory.CreateConnection(); // versão síncrona ainda existe
            using IModel channel = connection.CreateModel();            // cria o canal

            channel.QueueDeclare(queue: "minha_fila",
                                 durable: false,
                                 exclusive: false,
                                 autoDelete: false,
                                 arguments: null);

            var body = Encoding.UTF8.GetBytes(message);

            channel.BasicPublish(exchange: "",
                                 routingKey: "minha_fila",
                                 basicProperties: null,
                                 body: body);

            Console.WriteLine($"Mensagem enviada: {message}");
        }
    }
}
