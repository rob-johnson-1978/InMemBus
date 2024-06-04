﻿using System.Collections.Concurrent;
using InMemBus.MessageHandling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InMemBus.Hosting;

internal class InMemBusObserver(IServiceProvider rootServiceProvider) : BackgroundService
{
    private readonly ConcurrentDictionary<Type, Func<IServiceProvider, object, CancellationToken, Task>> messageHandlingTaskFuncs = [];

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var logger = rootServiceProvider.GetRequiredService<ILogger<InMemBusObserver>>();
        var inMemoryBus = rootServiceProvider.GetRequiredService<IInMemBus>();

        while (!cancellationToken.IsCancellationRequested)
        {
            var nextMessages = inMemoryBus.GetNextMessagesToProcess();

            foreach (var nextMessage in nextMessages)
            {
                _ = HandleMessageAsync(logger, inMemoryBus, nextMessage, cancellationToken);
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false); // blocks pipeline if not present
        }
    }

    private async Task HandleMessageAsync(
        ILogger<InMemBusObserver> logger, 
        IInMemBus inMemBus,
        object message, 
        CancellationToken cancellationToken)
    {
        try
        {
            var messageType = message.GetType();
            var messageHandlerType = typeof(MessageHandler<>).MakeGenericType(messageType);

            await using var scope = rootServiceProvider.CreateAsyncScope();

            var messageHandler = scope.ServiceProvider.GetRequiredService(messageHandlerType);

            var taskFuncExists = messageHandlingTaskFuncs.TryGetValue(messageHandlerType, out var taskFunc);

            if (!taskFuncExists || taskFunc == null)
            {
                const string methodName = nameof(MessageHandler<object>.HandleAsync);
                var method = messageHandlerType.GetMethod(methodName) ?? throw new Exception("Method does not exist - shouldn't be possible");
                
                taskFunc = async (sp, msg, cancel) =>
                {
                    if (method.Invoke(messageHandler, new[] { sp, msg, cancel }) is not Task task)
                    {
                        return;
                    }

                    await task.ConfigureAwait(false);
                };

                messageHandlingTaskFuncs.TryAdd(messageHandlerType, taskFunc);
            }

            await taskFunc.Invoke(scope.ServiceProvider, message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Parent-level error when calling message handling functionality - handling will not be retried");
        }
        finally
        {
            inMemBus.ReleaseHandlingSlot(message);
        }
    }
}