namespace VideoNest.Services {
    public interface IRabbitMQPublisher {
        void PublishMessage(string message);
    }
}