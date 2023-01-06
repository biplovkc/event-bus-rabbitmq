
# Rabbit MQ Event Bus

This project is based on [eshopOnContainers RabbitMQ](https://github.com/dotnet-architecture/eShopOnContainers/tree/dev/src/BuildingBlocks/EventBus/EventBusRabbitMQ) implementation and uses Serilog's logger for logging.

## How to install
```
dotnet add package Biplov.EventBus.RabbitMQ
```

## How to use
Register the RabbitMQ connection by invoking RegisterRabbitMqConnection function. For example:
```
services.RegisterRabbitMqConnection(Log.Logger, "foo.com",
        "testUser", "password", 3)
```

Register the EventBus
```
services.AddRabbitMqEventBus(Log.Logger, "myClientApp", 3)
```
