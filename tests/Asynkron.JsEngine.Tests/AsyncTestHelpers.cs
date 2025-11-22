using System.Threading.Tasks;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Asynkron.JsEngine.Execution;
using Asynkron.JsEngine.StdLib;

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
            var promiseConstructor = StandardLibrary.CreatePromiseConstructor(engine);
            if (promiseConstructor is not IJsCallable promiseCtor)
            {
                return null;
            }

            var executor = new HostFunction((_, execArgs) =>
            {
                if (execArgs.Count < 2 ||
                    execArgs[0] is not IJsCallable resolve ||
                    execArgs[1] is not IJsCallable reject)
                {
                    return null;
                }

                engine.ScheduleTask(async () =>
                {
                    try
                    {
                        await Task.Delay(ms).ConfigureAwait(false);
                        resolve.Invoke(new object?[] { value }, null);
                    }
                    catch (Exception ex)
                    {
                        reject.Invoke(new object?[] { ex.Message }, null);
                    }

                    await Task.CompletedTask.ConfigureAwait(false);
                });

                return null;
            });

            var promiseObj = promiseCtor.Invoke(new object?[] { executor }, null);
            return promiseObj;
        });
    }
}
