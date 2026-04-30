using System.Threading.Tasks;
using VideoNest.Services;
using Xunit;

namespace VideoNest.UnitTests.Repositories;

public class VideoRepositoryTests
{
    [Fact]
    public void RepositoryTests_Placeholder_ShouldPass()
    {
        Assert.True(true);
    }

    private sealed class FakeRabbitMQPublisher : IRabbitMQPublisher
    {
        public Task DeclareInfrastructureAsync()
            => Task.CompletedTask;

        public Task PublishVideoMessageAsync(object message)
            => Task.CompletedTask;

        public void PublishMessage(string message)
        {
        }

        public void Dispose()
        {
        }
    }
}