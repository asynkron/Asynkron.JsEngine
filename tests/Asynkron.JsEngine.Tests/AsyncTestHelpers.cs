using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests;

internal static class AsyncTestHelpers
{
    /// <summary>
    /// Registers a host-level __delay(ms, value) helper that returns a promise-like
    /// object (an object with a then(onFulfilled, onRejected) method). The promise
    /// resolves after the specified delay using the engine's event queue.
    /// </summary>
    public static void RegisterDelayHelper(JsEngine engine)
    {
        engine.SetGlobalFunction("__delay", args =>
        {
            var ms = 0;
            if (args.Count > 0 && args[0] is double delayMs)
            {
                ms = (int)delayMs;
            }

            var value = args.Count > 1 ? args[1] : null;
            var promiseLike = new JsObject();

            promiseLike.SetProperty("then", new HostFunction((thisValue, thenArgs) =>
            {
                var onFulfilled = thenArgs.Count > 0 ? thenArgs[0] as IJsCallable : null;
                var onRejected = thenArgs.Count > 1 ? thenArgs[1] as IJsCallable : null;

                engine.ScheduleTask(async () =>
                {
                    try
                    {
                        await Task.Delay(ms).ConfigureAwait(false);
                        onFulfilled?.Invoke(new object?[] { value }, promiseLike);
                    }
                    catch (Exception ex)
                    {
                        onRejected?.Invoke(new object?[] { ex.Message }, promiseLike);
                    }

                    await Task.CompletedTask.ConfigureAwait(false);
                });

                return promiseLike;
            }));

            return promiseLike;
        });
    }
}

