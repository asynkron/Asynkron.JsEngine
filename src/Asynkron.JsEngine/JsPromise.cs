namespace Asynkron.JsEngine;

/// <summary>
/// Represents a JavaScript Promise object that can be resolved or rejected.
/// </summary>
internal sealed class JsPromise
{
    private enum PromiseState
    {
        Pending,
        Fulfilled,
        Rejected
    }

    private PromiseState _state = PromiseState.Pending;
    private object? _value;
    private readonly List<(IJsCallable? onFulfilled, IJsCallable? onRejected, JsPromise next)> _handlers = [];
    private readonly JsEngine _engine;
    private readonly JsObject _jsObject;

    public JsPromise(JsEngine engine)
    {
        _engine = engine;
        _jsObject = new JsObject();
    }

    /// <summary>
    /// Gets the underlying JsObject for property access.
    /// </summary>
    public JsObject JsObject => _jsObject;

    /// <summary>
    /// Resolves the promise with the given value.
    /// </summary>
    public void Resolve(object? value)
    {
        if (_state != PromiseState.Pending)
            return;

        _state = PromiseState.Fulfilled;
        _value = value;
        ProcessHandlers();
    }

    /// <summary>
    /// Rejects the promise with the given reason.
    /// </summary>
    public void Reject(object? reason)
    {
        if (_state != PromiseState.Pending)
            return;

        _state = PromiseState.Rejected;
        _value = reason;
        ProcessHandlers();
    }

    /// <summary>
    /// Registers callbacks for when the promise is fulfilled or rejected.
    /// </summary>
    public JsPromise Then(IJsCallable? onFulfilled, IJsCallable? onRejected = null)
    {
        var nextPromise = new JsPromise(_engine);
        _handlers.Add((onFulfilled, onRejected, nextPromise));

        if (_state != PromiseState.Pending)
        {
            ProcessHandlers();
        }

        return nextPromise;
    }

    private void ProcessHandlers()
    {
        if (_state == PromiseState.Pending)
            return;

        var handlersToProcess = _handlers.ToList();
        _handlers.Clear();

        foreach (var (onFulfilled, onRejected, nextPromise) in handlersToProcess)
        {
            _engine.ScheduleTask(async () =>
            {
                try
                {
                    if (_state == PromiseState.Fulfilled)
                    {
                        if (onFulfilled != null)
                        {
                            var result = onFulfilled.Invoke([_value], null);
                            
                            // If the result is a promise (JsObject with "then" method), chain it
                            if (result is JsObject resultObj && resultObj.TryGetProperty("then", out var thenMethod) && thenMethod is IJsCallable thenCallable)
                            {
                                thenCallable.Invoke([
                                    new HostFunction(args =>
                                    {
                                        nextPromise.Resolve(args.Count > 0 ? args[0] : null);
                                        return null;
                                    }),
                                    new HostFunction(args =>
                                    {
                                        nextPromise.Reject(args.Count > 0 ? args[0] : null);
                                        return null;
                                    })
                                ], resultObj);
                            }
                            else
                            {
                                nextPromise.Resolve(result);
                            }
                        }
                        else
                        {
                            nextPromise.Resolve(_value);
                        }
                    }
                    else if (_state == PromiseState.Rejected)
                    {
                        if (onRejected != null)
                        {
                            var result = onRejected.Invoke([_value], null);
                            
                            // If the result is a promise (JsObject with "then" method), chain it
                            if (result is JsObject resultObj && resultObj.TryGetProperty("then", out var thenMethod) && thenMethod is IJsCallable thenCallable)
                            {
                                thenCallable.Invoke([
                                    new HostFunction(args =>
                                    {
                                        nextPromise.Resolve(args.Count > 0 ? args[0] : null);
                                        return null;
                                    }),
                                    new HostFunction(args =>
                                    {
                                        nextPromise.Reject(args.Count > 0 ? args[0] : null);
                                        return null;
                                    })
                                ], resultObj);
                            }
                            else
                            {
                                // Rejection handler executed successfully, resolve next promise
                                nextPromise.Resolve(result);
                            }
                        }
                        else
                        {
                            // No rejection handler, propagate rejection
                            nextPromise.Reject(_value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    nextPromise.Reject(ex.Message);
                }

                await Task.CompletedTask;
            });
        }
    }
}
