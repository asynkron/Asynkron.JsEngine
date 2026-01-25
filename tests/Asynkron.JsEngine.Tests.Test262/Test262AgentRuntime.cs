using System.Collections.Concurrent;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

namespace Asynkron.JsEngine.Tests.Test262;

internal sealed class Test262AgentRuntime
{
    private readonly Func<JsEngine> _createAgentEngine;
    private readonly IReadOnlyDictionary<string, string> _harnessSources;

    private readonly ConcurrentQueue<string> _reports = new();
    private readonly ManualResetEventSlim _sleepEvent = new(false);

    private readonly object _agentsLock = new();
    private readonly List<AgentInstance> _agents = new();

    internal Test262AgentRuntime(Func<JsEngine> createAgentEngine, IReadOnlyDictionary<string, string> harnessSources)
    {
        _createAgentEngine = createAgentEngine ?? throw new ArgumentNullException(nameof(createAgentEngine));
        _harnessSources = harnessSources ?? throw new ArgumentNullException(nameof(harnessSources));
    }

    internal JsObject CreateMainAgentObject()
    {
        return new JsObject
        {
            ["start"] = new HostFunction(StartAgent),
            ["broadcast"] = new HostFunction(Broadcast),
            ["getReport"] = new HostFunction(_ => GetReport()),
            ["sleep"] = new HostFunction(Sleep),
        };
    }

    private JsValue StartAgent(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || args[0].ToObject() is not string source)
        {
            return JsValue.Undefined;
        }

        var agent = new AgentInstance(this, source);
        lock (_agentsLock)
        {
            _agents.Add(agent);
        }

        agent.Start();
        return JsValue.Undefined;
    }

    private JsValue Broadcast(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0 || !args[0].TryGetObject<JsArrayBuffer>(out var buffer))
        {
            return JsValue.Undefined;
        }

        AgentInstance[] agents;
        lock (_agentsLock)
        {
            agents = _agents.ToArray();
        }

        foreach (var agent in agents)
        {
            agent.EnqueueBroadcast(buffer);
        }

        return JsValue.Undefined;
    }

    private JsValue Sleep(IReadOnlyList<JsValue> args)
    {
        var ms = 0d;
        if (args.Count > 0)
        {
            _ = args[0].TryGetDouble(out ms);
        }

        if (ms > 0)
        {
            _sleepEvent.Wait(TimeSpan.FromMilliseconds(ms));
        }

        return JsValue.Undefined;
    }

    private JsValue GetReport()
    {
        return _reports.TryDequeue(out var report) ? report : JsValue.Null;
    }

    private void Report(string report)
    {
        _reports.Enqueue(report);
    }

    private sealed class AgentInstance
    {
        private readonly Test262AgentRuntime _runtime;
        private readonly string _source;

        private readonly BlockingCollection<JsArrayBuffer> _broadcastQueue = new();
        private readonly ManualResetEventSlim _callbackReady = new(false);

        private IJsCallable? _broadcastCallback;
        private int _leaving;

        internal AgentInstance(Test262AgentRuntime runtime, string source)
        {
            _runtime = runtime;
            _source = source;
        }

        internal void Start()
        {
            var thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "test262-agent",
            };

            thread.Start();
        }

        internal void EnqueueBroadcast(JsArrayBuffer buffer)
        {
            if (_broadcastQueue.IsAddingCompleted)
            {
                return;
            }

            try
            {
                _broadcastQueue.Add(buffer);
            }
            catch
            {
                // Ignore enqueues after completion.
            }
        }

        private void Run()
        {
            var engine = _runtime._createAgentEngine();
            engine.GlobalObject["global"] = engine.GlobalObject;

            if (_runtime._harnessSources.TryGetValue("assert.js", out var assertSource))
            {
                engine.EvaluateSync(assertSource);
                engine.DrainMicrotasks();
            }

            if (_runtime._harnessSources.TryGetValue("sta.js", out var staSource))
            {
                engine.EvaluateSync(staSource);
                engine.DrainMicrotasks();
            }

            var agent = CreateWorkerAgentObject();
            var obj262 = new JsObject
            {
                ["agent"] = agent,
            };
            engine.SetGlobalValue("$262", obj262);

            try
            {
                engine.EvaluateSync(_source);
                engine.DrainMicrotasks();
            }
            catch (Exception ex)
            {
                _runtime.Report(ex.ToString());
                return;
            }

            JsArrayBuffer? pending = null;
            while (Volatile.Read(ref _leaving) == 0)
            {
                if (pending is null)
                {
                    try
                    {
                        pending = _broadcastQueue.Take();
                    }
                    catch
                    {
                        break;
                    }
                }

                if (_broadcastCallback is null)
                {
                    _callbackReady.Wait(TimeSpan.FromMilliseconds(10));
                    continue;
                }

                try
                {
                    _broadcastCallback.Invoke(new SingleValueArgs(JsValue.FromObjectUnsafe(pending)), JsValue.Undefined);
                    engine.DrainMicrotasks();
                }
                catch (Exception ex)
                {
                    _runtime.Report(ex.ToString());
                }

                pending = null;
            }
        }

        private JsObject CreateWorkerAgentObject()
        {
            return new JsObject
            {
                ["receiveBroadcast"] = new HostFunction(ReceiveBroadcast),
                ["report"] = new HostFunction(Report),
                ["sleep"] = new HostFunction(_runtime.Sleep),
                ["leaving"] = new HostFunction(Leaving),
            };
        }

        private JsValue ReceiveBroadcast(IReadOnlyList<JsValue> args)
        {
            if (args.Count == 0)
            {
                return JsValue.Undefined;
            }

            if (!args[0].TryGetObject<IJsCallable>(out var callback))
            {
                return JsValue.Undefined;
            }

            _broadcastCallback = callback;
            _callbackReady.Set();
            return JsValue.Undefined;
        }

        private JsValue Report(IReadOnlyList<JsValue> args)
        {
            var report = args.Count > 0 ? args[0].ToObject()?.ToString() ?? "" : "";
            _runtime.Report(report);
            return JsValue.Undefined;
        }

        private JsValue Leaving(IReadOnlyList<JsValue> _)
        {
            Interlocked.Exchange(ref _leaving, 1);
            _broadcastQueue.CompleteAdding();
            _callbackReady.Set();
            return JsValue.Undefined;
        }
    }
}
