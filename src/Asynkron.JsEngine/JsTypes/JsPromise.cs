using System.Linq;
using System.Threading.Tasks;

namespace Asynkron.JsEngine.JsTypes;

/// <summary>
///     Represents a JavaScript Promise object that can be resolved or rejected.
/// </summary>
public sealed class JsPromise
{
    private const string InternalPromiseKey = "__promise__";
    private readonly List<(IJsCallable? onFulfilled, IJsCallable? onRejected, JsPromise next)> _handlers = [];
    private bool _handlersScheduled;
    private readonly JsEngine _engine;

    private PromiseState _state = PromiseState.Pending;
    private object? _value;

    /// <summary>
    ///     Gets the underlying JsObject for property access.
    /// </summary>
    public JsObject JsObject { get; }

    public JsPromise(JsEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        JsObject = new JsObject();
        JsObject.DefineProperty(InternalPromiseKey, new PropertyDescriptor
        {
            Value = this, Writable = false, Enumerable = false, Configurable = false
        });
    }

    /// <summary>
    ///     Resolves the promise with the given value.
    /// </summary>
    public void Resolve(object? value)
    {
        if (_state != PromiseState.Pending)
        {
            return;
        }

        _state = PromiseState.Fulfilled;
        _value = value;
        ScheduleProcessing();
    }

    /// <summary>
    ///     Rejects the promise with the given reason.
    /// </summary>
    public void Reject(object? reason)
    {
        if (_state != PromiseState.Pending)
        {
            return;
        }

        _state = PromiseState.Rejected;
        _value = reason;
        ScheduleProcessing();
    }

    /// <summary>
    ///     Registers callbacks for when the promise is fulfilled or rejected.
    /// </summary>
    public JsPromise Then(IJsCallable? onFulfilled, IJsCallable? onRejected = null)
    {
        var nextPromise = new JsPromise(_engine);
        _handlers.Add((onFulfilled, onRejected, nextPromise));

        if (_state != PromiseState.Pending)
        {
            ScheduleProcessing();
        }

        return nextPromise;
    }

    internal bool TryGetSettled(out object? value, out bool isRejected)
    {
        if (_state == PromiseState.Pending)
        {
            value = null;
            isRejected = false;
            return false;
        }

        value = _value;
        isRejected = _state == PromiseState.Rejected;
        return true;
    }

    private void ScheduleProcessing()
    {
        if (_handlersScheduled)
        {
            return;
        }

        _handlersScheduled = true;
        _engine.ScheduleTask(() =>
        {
            try
            {
                ProcessHandlersCore();
            }
            finally
            {
                _handlersScheduled = false;
            }

            return Task.CompletedTask;
        });
    }

    private void ProcessHandlersCore()
    {
        if (_state == PromiseState.Pending)
        {
            return;
        }

        var handlersToProcess = _handlers.ToList();
        _handlers.Clear();

        foreach (var (onFulfilled, onRejected, nextPromise) in handlersToProcess)
        {
            try
            {
                if (++_engine.PromiseCallDepth > _engine.MaxCallDepth)
                {
                    throw new InvalidOperationException(
                        $"Exceeded maximum call depth of {_engine.MaxCallDepth} while resolving promise callbacks.");
                }

                if (_state == PromiseState.Fulfilled)
                {
                    if (onFulfilled != null)
                    {
                        var result = onFulfilled.Invoke([_value], null);

                        // If the result is a promise (JsObject with "then" method), chain it
                        if (result is JsObject resultObj && resultObj.TryGetProperty("then", out var thenMethod) &&
                            thenMethod is IJsCallable thenCallable)
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
                        if (result is JsObject resultObj && resultObj.TryGetProperty("then", out var thenMethod) &&
                            thenMethod is IJsCallable thenCallable)
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
                        // No rejection handler, propagate rejection
                        nextPromise.Reject(_value);
                    }
                }
            }
            catch (Exception ex)
            {
                nextPromise.Reject(ex.Message);
            }
            finally
            {
                _engine.PromiseCallDepth = Math.Max(0, _engine.PromiseCallDepth - 1);
            }
        }

        if (_handlers.Count > 0)
        {
            ScheduleProcessing();
        }
    }

    private enum PromiseState
    {
        Pending,
        Fulfilled,
        Rejected
    }
}
