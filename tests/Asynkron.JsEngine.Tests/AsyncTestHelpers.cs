using Asynkron.JsEngine.JsTypes;
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
            if (args.Count > 0 && args[0].TryGetDouble(out var delayMs))
            {
                ms = (int)delayMs;
            }

            var value = args.Count > 1 ? args[1] : JsValue.Undefined;
            var promiseConstructor = StandardLibrary.CreatePromiseConstructor(engine.RealmState);
            if (!JsValue.FromObject(promiseConstructor).TryGetObject<IJsCallable>(out var promiseCtor))
            {
                return JsValue.Undefined;
            }

            var executor = new HostFunction((_, execArgs) =>
            {
                if (execArgs.Count < 2 ||
                    !execArgs[0].TryGetObject<IJsCallable>(out var resolve) ||
                    !execArgs[1].TryGetObject<IJsCallable>(out var reject))
                {
                    return JsValue.Undefined;
                }

                engine.ScheduleTask(async () =>
                {
                    try
                    {
                        await Task.Delay(ms).ConfigureAwait(false);
                        resolve.Invoke([value], JsValue.Undefined);
                    }
                    catch (Exception ex)
                    {
                        reject.Invoke([new JsValue(ex.Message)], JsValue.Undefined);
                    }

                    await Task.CompletedTask.ConfigureAwait(false);
                });

                return JsValue.Undefined;
            });

            if (promiseCtor is HostFunction hostCtor)
            {
                return hostCtor.InvokeWithContext([JsValue.FromObject(executor)], JsValue.Undefined, null, JsValue.FromObject(hostCtor));
            }

            return promiseCtor.Invoke([JsValue.FromObject(executor)], JsValue.Undefined);
        });
    }
}
