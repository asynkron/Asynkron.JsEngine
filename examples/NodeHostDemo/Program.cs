using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

var scriptPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(AppContext.BaseDirectory, "scripts", "server.js");

if (!File.Exists(scriptPath))
{
    Console.Error.WriteLine($"Script not found: {scriptPath}");
    Environment.ExitCode = 1;
    return;
}

using var shutdown = new CancellationTokenSource();
using var sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, context =>
{
    context.Cancel = true;
    shutdown.Cancel();
});
using var sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    shutdown.Cancel();
});

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};

await using var runtime = new MiniNodeRuntime(scriptPath);
await runtime.RunAsync(shutdown.Token).ConfigureAwait(false);

if (runtime.HasActiveServers)
{
    Console.WriteLine("Press Ctrl+C to stop.");
    await MiniNodeRuntime.WaitForShutdownAsync(shutdown.Token).ConfigureAwait(false);
}

internal sealed class MiniNodeRuntime : IAsyncDisposable
{
    private readonly JsEngine _engine = new();
    private readonly string _scriptDirectory;
    private readonly string _scriptPath;
    private readonly List<MiniHttpServer> _servers = [];
    private readonly Dictionary<string, JsObject> _moduleCache = new(StringComparer.Ordinal);

    public MiniNodeRuntime(string scriptPath)
    {
        _scriptPath = scriptPath;
        _scriptDirectory = Path.GetDirectoryName(scriptPath) ?? Directory.GetCurrentDirectory();
        _engine.SetGlobalFunction("require", Require);
    }

    public bool HasActiveServers => _servers.Count > 0;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var source = await File.ReadAllTextAsync(_scriptPath, cancellationToken).ConfigureAwait(false);
        await _engine.Evaluate(source, cancellationToken).ConfigureAwait(false);
    }

    public static async Task WaitForShutdownAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var server in _servers)
        {
            await server.DisposeAsync().ConfigureAwait(false);
        }

        _engine.Dispose();
    }

    private JsValue Require(IReadOnlyList<JsValue> args)
    {
        var moduleName = GetRequiredString(args, 0, "require");
        if (_moduleCache.TryGetValue(moduleName, out var cached))
        {
            return cached;
        }

        var module = moduleName switch
        {
            "fs" => CreateFsModule(),
            "http" => CreateHttpModule(),
            _ => throw new NotSupportedException($"Module '{moduleName}' is not available in this demo host.")
        };

        _moduleCache[moduleName] = module;
        return module;
    }

    private JsObject CreateFsModule()
    {
        var fs = new JsObject();
        SetProperty(fs, "readFileSync", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.readFileSync");
            var resolvedPath = ResolveScriptPath(requestedPath);
            return File.ReadAllText(resolvedPath);
        }));
        return fs;
    }

    private JsObject CreateHttpModule()
    {
        var http = new JsObject();
        SetProperty(http, "createServer", CreateHostFunction(args =>
        {
            if (args.Count == 0 || !args[0].TryGetCallable(out var callback))
            {
                throw new ArgumentException("http.createServer requires a request handler function.");
            }

            return CreateServerObject(callback);
        }));
        return http;
    }

    private JsObject CreateServerObject(IJsCallable callback)
    {
        var server = new MiniHttpServer(this, callback);
        var serverObject = new JsObject();

        SetProperty(serverObject, "listen", CreateHostFunction((thisValue, args) =>
        {
            var port = GetRequiredInt(args, 0, "server.listen");
            server.Listen(port);
            _servers.Add(server);
            return thisValue.IsUndefined ? serverObject : thisValue;
        }));

        SetProperty(serverObject, "close", CreateHostFunction((thisValue, _) =>
        {
            server.Stop();
            return thisValue.IsUndefined ? serverObject : thisValue;
        }));

        return serverObject;
    }

    public void DispatchRequest(IJsCallable callback, HttpListenerContext context, TaskCompletionSource completion)
    {
        _engine.ScheduleTask(() =>
        {
            var response = new ResponseHost(context.Response);
            try
            {
                callback.Invoke(
                    [(JsValue)CreateRequestObject(context.Request), (JsValue)response.CreateResponseObject(this)],
                    JsValue.Undefined);

                if (!response.HasEnded)
                {
                    response.End(JsValue.EmptyString);
                }

                completion.SetResult();
            }
            catch (Exception ex)
            {
                if (!response.HasEnded)
                {
                    response.SendError(500, ex.Message);
                }

                completion.SetException(ex);
            }
        });
    }

    private static JsObject CreateRequestObject(HttpListenerRequest request)
    {
        var headers = new JsObject();
        foreach (var key in request.Headers.AllKeys)
        {
            if (key is null)
            {
                continue;
            }

            SetProperty(headers, key.ToLowerInvariant(), request.Headers[key] ?? string.Empty);
        }

        var req = new JsObject();
        SetProperty(req, "method", request.HttpMethod);
        SetProperty(req, "url", request.RawUrl ?? request.Url?.PathAndQuery ?? "/");
        SetProperty(req, "headers", headers);
        return req;
    }

    private string ResolveScriptPath(string requestedPath)
    {
        if (Path.IsPathRooted(requestedPath))
        {
            return requestedPath;
        }

        return Path.GetFullPath(Path.Combine(_scriptDirectory, requestedPath));
    }

    private HostFunction CreateHostFunction(JsSimpleHandler handler)
    {
        return new HostFunction(handler, realmState: null, isConstructor: false)
        {
            Realm = _engine.GlobalObject
        };
    }

    private HostFunction CreateHostFunction(JsHostHandler handler)
    {
        return new HostFunction(handler, realmState: null, isConstructor: false)
        {
            Realm = _engine.GlobalObject
        };
    }

    private static void SetProperty(JsObject target, string name, JsValue value)
    {
        target.DefineProperty(name,
            new PropertyDescriptor
            {
                JsValue = value,
                Writable = true,
                Enumerable = true,
                Configurable = true
            });
    }

    private static string GetRequiredString(IReadOnlyList<JsValue> args, int index, string functionName)
    {
        if (args.Count <= index)
        {
            throw new ArgumentException($"{functionName} requires argument {index.ToString(CultureInfo.InvariantCulture)}.");
        }

        return ToHostString(args[index]);
    }

    private static int GetRequiredInt(IReadOnlyList<JsValue> args, int index, string functionName)
    {
        if (args.Count <= index || !args[index].TryGetDouble(out var value))
        {
            throw new ArgumentException($"{functionName} requires a numeric argument.");
        }

        return (int)value;
    }

    private static string ToHostString(JsValue value)
    {
        if (value.TryGetString(out var text))
        {
            return text;
        }

        if (value.IsNullOrUndefined)
        {
            return string.Empty;
        }

        return value.ToString();
    }

    private sealed class MiniHttpServer : IAsyncDisposable
    {
        private readonly IJsCallable _callback;
        private readonly CancellationTokenSource _cts = new();
        private readonly HttpListener _listener = new();
        private readonly MiniNodeRuntime _runtime;
        private Task? _listenTask;

        public MiniHttpServer(MiniNodeRuntime runtime, IJsCallable callback)
        {
            _runtime = runtime;
            _callback = callback;
        }

        public void Listen(int port)
        {
            if (_listenTask is not null)
            {
                throw new InvalidOperationException("Server is already listening.");
            }

            var portText = port.ToString(CultureInfo.InvariantCulture);
            _listener.Prefixes.Add($"http://localhost:{portText}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{portText}/");
            _listener.Start();
            _listenTask = ListenLoopAsync(_cts.Token);
            Console.WriteLine($"Listening on http://localhost:{portText}/");
        }

        public void Stop()
        {
            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }

            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }

        public async ValueTask DisposeAsync()
        {
            Stop();
            if (_listenTask is not null)
            {
                await _listenTask.ConfigureAwait(false);
            }

            _listener.Close();
            _cts.Dispose();
        }

        private async Task ListenLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (cancellationToken.IsCancellationRequested || !_listener.IsListening)
                {
                    break;
                }

                _ = HandleRequestAsync(context, cancellationToken);
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _runtime.DispatchRequest(_callback, context, completion);
                await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Request failed: {ex.Message}");
            }
        }
    }

    private sealed class ResponseHost
    {
        private readonly HttpListenerResponse _response;

        public ResponseHost(HttpListenerResponse response)
        {
            _response = response;
            _response.StatusCode = 200;
        }

        public bool HasEnded { get; private set; }

        public JsObject CreateResponseObject(MiniNodeRuntime runtime)
        {
            var response = new JsObject();
            SetProperty(response, "writeHead", runtime.CreateHostFunction(args =>
            {
                WriteHead(args);
                return JsValue.Undefined;
            }));

            SetProperty(response, "end", runtime.CreateHostFunction(args =>
            {
                End(args.Count > 0 ? args[0] : JsValue.EmptyString);
                return JsValue.Undefined;
            }));

            return response;
        }

        public void WriteHead(IReadOnlyList<JsValue> args)
        {
            if (HasEnded)
            {
                return;
            }

            if (args.Count > 0 && args[0].TryGetDouble(out var statusCode))
            {
                _response.StatusCode = (int)statusCode;
            }

            if (args.Count <= 1 || !args[1].TryGetObject<JsObject>(out var headers))
            {
                return;
            }

            foreach (var key in headers.Keys)
            {
                if (!headers.TryGetProperty(key, out var value))
                {
                    continue;
                }

                SetHeader(key, ToHostString(value));
            }
        }

        public void End(JsValue body)
        {
            if (HasEnded)
            {
                return;
            }

            var text = ToHostString(body);
            var bytes = Encoding.UTF8.GetBytes(text);
            _response.ContentLength64 = bytes.Length;
            _response.OutputStream.Write(bytes, 0, bytes.Length);
            _response.OutputStream.Close();
            HasEnded = true;
        }

        public void SendError(int statusCode, string message)
        {
            if (HasEnded)
            {
                return;
            }

            _response.StatusCode = statusCode;
            _response.ContentType = "text/plain; charset=utf-8";
            End(message);
        }

        private void SetHeader(string key, string value)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                _response.ContentType = value;
                return;
            }

            _response.Headers[key] = value;
        }
    }
}
