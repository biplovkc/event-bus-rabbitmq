﻿namespace Biplov.EventBus.RabbitMQ;

public class EventBusRabbitMQ : IEventBus, IDisposable
{
    const string BROKER_NAME = "biplov_event_bus";

    private readonly IRabbitMQPersistentConnection _persistentConnection;
    private readonly ILogger _logger;
    private readonly IEventBusSubscriptionsManager _subsManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _retryCount;

    private IModel _consumerChannel;
    private string _queueName;

    public EventBusRabbitMQ(IRabbitMQPersistentConnection persistentConnection, ILogger logger,
        IServiceProvider serviceProvider, IEventBusSubscriptionsManager subsManager, string queueName = null, int retryCount = 5)
    {
        _persistentConnection = persistentConnection ?? throw new ArgumentNullException(nameof(persistentConnection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _subsManager = subsManager ?? new InMemoryEventBusSubscriptionsManager();
        _queueName = queueName;
        _consumerChannel = CreateConsumerChannel();
        _serviceProvider = serviceProvider;
        _retryCount = retryCount;
        _subsManager.OnEventRemoved += SubsManager_OnEventRemoved;
    }
    public void Publish(IntegrationEvent @event)
    {
        if (!_persistentConnection.IsConnected)
            _persistentConnection.TryConnect();

        var policy = RetryPolicy.Handle<BrokerUnreachableException>()
            .Or<SocketException>()
            .WaitAndRetry(_retryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (ex, time) =>
            {
                _logger.Warning(ex, "Could not publish event: {EventId} after {Timeout}s ({ExceptionMessage})", @event.Id, $"{time.TotalSeconds:n1}", ex.Message);
            });

        var eventName = @event.GetType().Name;

        _logger.Debug("Creating RabbitMQ channel to publish event: {EventId} ({EventName})", @event.Id, eventName);

        using var channel = _persistentConnection.CreateModel();
        _logger.Debug("Declaring RabbitMQ exchange to publish event: {EventId}", @event.Id);

        channel.ExchangeDeclare(exchange: BROKER_NAME, type: "direct");

        var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType(), new JsonSerializerOptions
        {
            WriteIndented = true
        });

        policy.Execute(() =>
        {
            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // persistent

            _logger.Debug("Publishing event to RabbitMQ: {EventId}", @event.Id);

            channel.BasicPublish(
                exchange: BROKER_NAME, 
                routingKey: eventName, 
                mandatory: true, 
                basicProperties: properties, 
                body: body);
        });
    }

    public void Publish(IEnumerable<IntegrationEvent> events)
    {
        foreach (var @event in events)
            Publish(@event);
    }

    public void SubscribeDynamic<TH>(string eventName)
        where TH : IDynamicIntegrationEventHandler
    {
        _logger.Information("Subscribing to dynamic event {EventName} with {EventHandler}", eventName, typeof(TH).GetGenericTypeName());

        DoInternalSubscription(eventName);
        _subsManager.AddDynamicSubscription<TH>(eventName);
        StartBasicConsume();
    }

    public void Subscribe<T, TH>()
        where T : IntegrationEvent
        where TH : IIntegrationEventHandler<T>
    {
        var eventName = _subsManager.GetEventKey<T>();
        DoInternalSubscription(eventName);

        _logger.Information("Subscribing to event {EventName} with {EventHandler}", eventName, typeof(TH).GetGenericTypeName());

        _subsManager.AddSubscription<T, TH>();
        StartBasicConsume();
    }

    private void DoInternalSubscription(string eventName)
    {
        var containsKey = _subsManager.HasSubscriptionsForEvent(eventName);
        
        if (containsKey) 
            return;
        
        if (!_persistentConnection.IsConnected)
            _persistentConnection.TryConnect();

        _consumerChannel.QueueBind(queue: _queueName,
            exchange: BROKER_NAME,
            routingKey: eventName);
    }

    public void Unsubscribe<T, TH>()
        where T : IntegrationEvent
        where TH : IIntegrationEventHandler<T>
    {
        var eventName = _subsManager.GetEventKey<T>();

        _logger.Information("Unsubscribing from event {EventName}", eventName);

        _subsManager.RemoveSubscription<T, TH>();
    }

    public void UnsubscribeDynamic<TH>(string eventName)
        where TH : IDynamicIntegrationEventHandler
    {
        _subsManager.RemoveDynamicSubscription<TH>(eventName);
    }
    private void SubsManager_OnEventRemoved(object sender, string eventName)
    {
        if (!_persistentConnection.IsConnected)
            _persistentConnection.TryConnect();

        using var channel = _persistentConnection.CreateModel();
        channel.QueueUnbind(queue: _queueName, exchange: BROKER_NAME, routingKey: eventName);
        if (!_subsManager.IsEmpty) return;
        _queueName = string.Empty;
        _consumerChannel.Close();
    }
    private IModel CreateConsumerChannel()
    {
        if (!_persistentConnection.IsConnected)
            _persistentConnection.TryConnect();

        _logger.Debug("Creating RabbitMQ consumer channel");

        var channel = _persistentConnection.CreateModel();
        
        channel.ExchangeDeclare(exchange: BROKER_NAME, type: "direct");
        
        channel.QueueDeclare(queue: _queueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
        
        channel.CallbackException += (sender, ea) =>
        {
            _logger.Warning(ea.Exception, "Recreating RabbitMQ consumer channel");

            _consumerChannel.Dispose();
            _consumerChannel = CreateConsumerChannel();
            StartBasicConsume();
        };

        return channel;
    }
    private void StartBasicConsume()
    {
        _logger.Debug("Starting RabbitMQ basic consume");

        if (_consumerChannel is not null)
        {
            var consumer = new AsyncEventingBasicConsumer(_consumerChannel);

            consumer.Received += Consumer_Received;
            
            _consumerChannel.BasicConsume(
                queue: _queueName, 
                autoAck: false, 
                consumer: consumer);
        }
        else
        {
            _logger.Error("StartBasicConsume can't call on _consumerChannel == null");
        }
    }

    private async Task Consumer_Received(object sender, BasicDeliverEventArgs eventArgs)
    {
        var eventName = eventArgs.RoutingKey;
        var message = Encoding.UTF8.GetString(eventArgs.Body.ToArray());

        try
        {
            if (message.ToLowerInvariant().Contains("throw-fake-exception"))
                throw new InvalidOperationException($"Fake exception requested: \"{message}\"");

            await ProcessEvent(eventName, message);
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "----- ERROR Processing message \"{Message}\"", message);
        }

        // Even on exception we take the message off the queue.
        // in a REAL WORLD app this should be handled with a Dead Letter Exchange (DLX). 
        // For more information see: https://www.rabbitmq.com/dlx.html
        _consumerChannel.BasicAck(eventArgs.DeliveryTag, multiple: false);
    }

    private async Task ProcessEvent(string eventName, string message)
    {
        _logger.Verbose("Processing RabbitMQ event: {EventName}", eventName);

        if (_subsManager.HasSubscriptionsForEvent(eventName))
        {
            using var scope = _serviceProvider.CreateScope();
            var subscriptions = _subsManager.GetHandlersForEvent(eventName);
            foreach (var subscription in subscriptions)
            {
                if (subscription.IsDynamic)
                {
                    var handler = scope.ServiceProvider.GetService(subscription.HandlerType) as IDynamicIntegrationEventHandler;
                    if (handler == null) continue;
                    using dynamic eventData = JsonDocument.Parse(message);
                    await Task.Yield();
                    await handler.Handle(eventData);
                }
                else
                {
                    var handler = scope.ServiceProvider.GetService(subscription.HandlerType);
                    if (handler == null) continue;
                    var eventType = _subsManager.GetEventTypeByName(eventName);
                    var integrationEvent = JsonSerializer.Deserialize(message, eventType);
                    var concreteType = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);

                    await Task.Yield();
                    await (Task)concreteType.GetMethod("Handle").Invoke(handler, new object[] { integrationEvent });
                }
            }
        }
        else
        {
            _logger.Warning("No subscription for RabbitMQ event: {EventName}", eventName);
        }
    }

    public void Dispose()
    {
        _consumerChannel?.Dispose();
        _subsManager.Clear();
    }
}