
# Rabbit MQ Event Bus

<h3 align="center">

  [![NuGet](https://img.shields.io/nuget/v/Biplov.EventBus.RabbitMQ.svg)](https://www.nuget.org/packages/Biplov.EventBus.RabbitMQ/)
  [![Downloads](https://img.shields.io/nuget/dt/Biplov.EventBus.RabbitMQ.svg)](https://www.nuget.org/packages/Biplov.EventBus.RabbitMQ/)
  [![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.md)

</h3>

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
