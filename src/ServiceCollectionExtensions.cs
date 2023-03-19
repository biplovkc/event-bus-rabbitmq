namespace Biplov.EventBus.RabbitMQ;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddRabbitMqEventBus(this IServiceCollection services, ILogger logger, string subscriptionClientName, int serviceBusRetryCount = 5) =>
        services.AddSingleton<IEventBus, EventBusRabbitMQ>(sp =>
        {
            var rabbitMqPersistentConnection = sp.GetRequiredService<IRabbitMQPersistentConnection>();
            var iLifetimeScope = sp.GetRequiredService<IServiceProvider>();
            var eventBusSubscriptionsManager = sp.GetRequiredService<IEventBusSubscriptionsManager>();

            return new EventBusRabbitMQ(rabbitMqPersistentConnection, logger, iLifetimeScope, eventBusSubscriptionsManager, subscriptionClientName, serviceBusRetryCount);
        });

    public static IServiceCollection RegisterRabbitMqConnection(this IServiceCollection services, ILogger logger, string hostName,
        string userName, string password, string vHost = null, int port = 5672, int retryCount = 5)
    {
        return services.AddSingleton<IRabbitMQPersistentConnection>(_ =>
        {
            var factory = new ConnectionFactory
            {
                HostName = hostName,
                DispatchConsumersAsync = true,
                Port = port
            };

            if (!string.IsNullOrWhiteSpace(userName))
                factory.UserName = userName;

            if (!string.IsNullOrWhiteSpace(password))
                factory.Password = password;

            if (!string.IsNullOrWhiteSpace(vHost))
                factory.VirtualHost = vHost;
            
            return new DefaultRabbitMQPersistentConnection(factory, logger, retryCount);
        });
    }
}