namespace Biplov.EventBus.RabbitMQ;

public static class Extensions
{
    public static IServiceCollection AddRabbitMqEventBus(this IServiceCollection services, ILogger logger, string subscriptionClientName, int serviceBusRetryCount = 5) =>
        services.AddSingleton<IEventBus, EventBusRabbitMQ>(sp =>
        {
            var rabbitMqPersistentConnection = sp.GetRequiredService<IRabbitMQPersistentConnection>();
            var iLifetimeScope = sp.GetRequiredService<ILifetimeScope>();
            var eventBusSubscriptionsManager = sp.GetRequiredService<IEventBusSubscriptionsManager>();

            return new EventBusRabbitMQ(rabbitMqPersistentConnection, logger, iLifetimeScope, eventBusSubscriptionsManager, subscriptionClientName, serviceBusRetryCount);
        });

    public static IServiceCollection RegisterRabbitMqConnection(this IServiceCollection services, ILogger logger, string hostName,
        string userName, string password, int retryCount = 5)
    {
        return services.AddSingleton<IRabbitMQPersistentConnection>(_ =>
        {
            var factory = new ConnectionFactory
            {
                HostName = hostName,
                DispatchConsumersAsync = true,
                UserName = userName,
                Password = password
            };
            return new DefaultRabbitMQPersistentConnection(factory, logger, retryCount);
        });
    }
}