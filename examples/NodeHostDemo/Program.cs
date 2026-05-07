using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly Stack<string> _requireDirectoryStack = new();
    private readonly string _scriptDirectory;
    private readonly string _scriptPath;
    private readonly List<MiniHttpServer> _servers = [];
    private readonly Dictionary<string, JsValue> _moduleCache = new(StringComparer.Ordinal);

    public MiniNodeRuntime(string scriptPath)
    {
        _scriptPath = scriptPath;
        _scriptDirectory = Path.GetDirectoryName(scriptPath) ?? Directory.GetCurrentDirectory();
        _engine.SetGlobalFunction("require", Require);
        _engine.SetGlobalValue("process", CreateProcessObject());
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
        if (TryCreateBuiltInModule(moduleName, out var builtInModule))
        {
            return builtInModule;
        }

        return LoadScriptModule(moduleName);
    }

    private bool TryCreateBuiltInModule(string moduleName, out JsValue module)
    {
        if (_moduleCache.TryGetValue(moduleName, out module))
        {
            return true;
        }

        module = moduleName switch
        {
            "fs" => CreateFsModule(),
            "http" => CreateHttpModule(),
            "path" => CreatePathModule(),
            "querystring" => CreateQueryStringModule(this),
            _ => JsValue.Undefined
        };

        if (module.IsUndefined)
        {
            return false;
        }

        _moduleCache[moduleName] = module;
        return true;
    }

    private JsObject CreateProcessObject()
    {
        var process = new JsObject();
        SetProperty(process, "uptime", CreateHostFunction(_ =>
        {
            var elapsed = DateTimeOffset.UtcNow - _startedAt;
            return JsValue.FromDouble(elapsed.TotalSeconds);
        }));
        return process;
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

        SetProperty(fs, "writeFileSync", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.writeFileSync");
            var contents = args.Count > 1 ? ToHostString(args[1]) : string.Empty;
            var resolvedPath = ResolveScriptPath(requestedPath);
            File.WriteAllText(resolvedPath, contents);
            return JsValue.Undefined;
        }));

        SetProperty(fs, "existsSync", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.existsSync");
            var resolvedPath = ResolveScriptPath(requestedPath);
            return File.Exists(resolvedPath) || Directory.Exists(resolvedPath);
        }));

        return fs;
    }

    private JsObject CreatePathModule()
    {
        var path = new JsObject();
        SetProperty(path, "join", CreateHostFunction(args =>
        {
            var parts = new string[args.Count];
            for (var i = 0; i < args.Count; i++)
            {
                parts[i] = ToHostString(args[i]);
            }

            return Path.Combine(parts);
        }));

        SetProperty(path, "basename", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "path.basename");
            return Path.GetFileName(requestedPath);
        }));

        SetProperty(path, "dirname", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "path.dirname");
            return Path.GetDirectoryName(requestedPath) ?? ".";
        }));

        SetProperty(path, "extname", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "path.extname");
            return Path.GetExtension(requestedPath);
        }));

        return path;
    }

    private JsObject CreateHttpModule()
    {
        var http = new JsObject();
        SetProperty(http, "createServer", CreateHostFunction(args =>
        {
            TryGetCallable(args, 0, out var callback);
            return CreateServerObject(callback);
        }));

        SetProperty(http, "STATUS_CODES", CreateStatusCodesObject());
        return http;
    }

    private static JsObject CreateStatusCodesObject()
    {
        var statusCodes = new JsObject();
        SetProperty(statusCodes, "200", "OK");
        SetProperty(statusCodes, "201", "Created");
        SetProperty(statusCodes, "204", "No Content");
        SetProperty(statusCodes, "400", "Bad Request");
        SetProperty(statusCodes, "404", "Not Found");
        SetProperty(statusCodes, "500", "Internal Server Error");
        return statusCodes;
    }

    private static JsObject CreateQueryStringModule(MiniNodeRuntime runtime)
    {
        var querystring = new JsObject();
        SetProperty(querystring, "parse", runtime.CreateHostFunction(args =>
        {
            var query = args.Count > 0 ? ToHostString(args[0]) : string.Empty;
            if (query.StartsWith("?", StringComparison.Ordinal))
            {
                query = query[1..];
            }

            return (JsValue)ParseQueryString(query);
        }));
        return querystring;
    }

    private JsObject CreateServerObject(IJsCallable? callback)
    {
        var server = new MiniHttpServer(this, callback);
        var serverObject = new JsObject();

        SetProperty(serverObject, "on", CreateHostFunction((thisValue, args) =>
        {
            var eventName = GetRequiredString(args, 0, "server.on");
            if (!string.Equals(eventName, "request", StringComparison.Ordinal))
            {
                return thisValue.IsUndefined ? serverObject : thisValue;
            }

            if (!TryGetCallable(args, 1, out var handler))
            {
                throw new ArgumentException("server.on('request') requires a handler function.");
            }

            server.SetRequestHandler(handler);
            return thisValue.IsUndefined ? serverObject : thisValue;
        }));

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

    private JsValue LoadScriptModule(string moduleName)
    {
        var modulePath = ResolveModulePath(moduleName);
        if (_moduleCache.TryGetValue(modulePath, out var cached))
        {
            return cached;
        }

        var exports = new JsObject();
        var module = new JsObject();
        SetProperty(module, "exports", exports);
        SetProperty(module, "filename", modulePath);
        SetProperty(module, "dirname", Path.GetDirectoryName(modulePath) ?? _scriptDirectory);

        // Cache before evaluating so cyclic requires get the partially initialized exports object.
        _moduleCache[modulePath] = exports;

        _requireDirectoryStack.Push(Path.GetDirectoryName(modulePath) ?? _scriptDirectory);
        try
        {
            var source = File.ReadAllText(modulePath);
            var wrappedSource =
                "(function (exports, require, module, __filename, __dirname) {\n" +
                source +
                "\n})";

            var factoryValue = JsValue.FromObjectUnsafe(_engine.EvaluateSync(wrappedSource));
            if (!factoryValue.TryGetCallable(out var factory))
            {
                throw new InvalidOperationException($"Module '{modulePath}' did not compile to a callable wrapper.");
            }

            var moduleDirectory = Path.GetDirectoryName(modulePath) ?? _scriptDirectory;
            factory.Invoke(
                [
                    (JsValue)exports,
                    (JsValue)CreateHostFunction(Require),
                    (JsValue)module,
                    (JsValue)modulePath,
                    (JsValue)moduleDirectory
                ],
                JsValue.Undefined);
        }
        finally
        {
            _requireDirectoryStack.Pop();
        }

        var exported = module.TryGetProperty("exports", out var moduleExports)
            ? moduleExports
            : (JsValue)exports;

        _moduleCache[modulePath] = exported;
        return exported;
    }

    public void DispatchRequest(
        IJsCallable callback,
        HttpListenerContext context,
        string requestBody,
        TaskCompletionSource completion)
    {
        _engine.ScheduleTask(() =>
        {
            var response = new ResponseHost(context.Response);
            try
            {
                callback.Invoke(
                    [(JsValue)CreateRequestObject(context.Request, requestBody), (JsValue)response.CreateResponseObject(this)],
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

    private static JsObject CreateRequestObject(HttpListenerRequest request, string body)
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
        SetProperty(req, "body", body);
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

    private string ResolveModulePath(string requestedPath)
    {
        var baseDirectory = _requireDirectoryStack.Count > 0
            ? _requireDirectoryStack.Peek()
            : _scriptDirectory;

        if (!IsScriptModuleSpecifier(requestedPath))
        {
            return ResolvePackageModulePath(requestedPath, baseDirectory);
        }

        var resolvedPath = Path.IsPathRooted(requestedPath)
            ? requestedPath
            : Path.GetFullPath(Path.Combine(baseDirectory, requestedPath));

        return ResolveFileOrDirectoryModulePath(requestedPath, resolvedPath);
    }

    private static string ResolvePackageModulePath(string moduleName, string baseDirectory)
    {
        var current = new DirectoryInfo(baseDirectory);
        while (current is not null)
        {
            var modulePath = Path.Combine(current.FullName, "node_modules", moduleName);
            if (Directory.Exists(modulePath) || File.Exists(modulePath))
            {
                return ResolveFileOrDirectoryModulePath(moduleName, modulePath);
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Cannot find module '{moduleName}'.", moduleName);
    }

    private static string ResolveFileOrDirectoryModulePath(string requestedPath, string resolvedPath)
    {
        if (File.Exists(resolvedPath))
        {
            return resolvedPath;
        }

        if (Path.GetExtension(resolvedPath).Length == 0)
        {
            var jsPath = resolvedPath + ".js";
            if (File.Exists(jsPath))
            {
                return jsPath;
            }
        }

        if (Directory.Exists(resolvedPath))
        {
            var packageJsonPath = Path.Combine(resolvedPath, "package.json");
            if (File.Exists(packageJsonPath))
            {
                var packageMain = GetPackageMain(packageJsonPath);
                var packageMainPath = Path.GetFullPath(Path.Combine(resolvedPath, packageMain));
                return ResolveFileOrDirectoryModulePath(requestedPath, packageMainPath);
            }

            var indexPath = Path.Combine(resolvedPath, "index.js");
            if (File.Exists(indexPath))
            {
                return indexPath;
            }
        }

        throw new FileNotFoundException($"Cannot find module '{requestedPath}'.", resolvedPath);
    }

    private static string GetPackageMain(string packageJsonPath)
    {
        using var packageJson = JsonDocument.Parse(File.ReadAllText(packageJsonPath));
        if (packageJson.RootElement.TryGetProperty("main", out var mainProperty) &&
            mainProperty.ValueKind == JsonValueKind.String)
        {
            return mainProperty.GetString() ?? "index.js";
        }

        return "index.js";
    }

    private static bool IsScriptModuleSpecifier(string moduleName)
    {
        return moduleName.StartsWith("./", StringComparison.Ordinal) ||
               moduleName.StartsWith("../", StringComparison.Ordinal) ||
               Path.IsPathRooted(moduleName);
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

    private static JsObject ParseQueryString(string query)
    {
        var result = new JsObject();
        if (query.Length == 0)
        {
            return result;
        }

        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var equalsIndex = pair.IndexOf('=');
            var key = equalsIndex < 0 ? pair : pair[..equalsIndex];
            var value = equalsIndex < 0 ? string.Empty : pair[(equalsIndex + 1)..];
            key = WebUtility.UrlDecode(key.Replace("+", "%2B", StringComparison.Ordinal)) ?? string.Empty;
            value = WebUtility.UrlDecode(value.Replace("+", " ", StringComparison.Ordinal)) ?? string.Empty;
            SetProperty(result, key, value);
        }

        return result;
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

    private static bool TryGetCallable(IReadOnlyList<JsValue> args, int index, out IJsCallable callable)
    {
        if (args.Count > index && args[index].TryGetCallable(out callable!))
        {
            return true;
        }

        callable = null!;
        return false;
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
        private readonly CancellationTokenSource _cts = new();
        private readonly HttpListener _listener = new();
        private readonly MiniNodeRuntime _runtime;
        private IJsCallable? _callback;
        private Task? _listenTask;

        public MiniHttpServer(MiniNodeRuntime runtime, IJsCallable? callback)
        {
            _runtime = runtime;
            _callback = callback;
        }

        public void SetRequestHandler(IJsCallable callback)
        {
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
                var requestBody = await ReadRequestBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
                if (_callback is null)
                {
                    context.Response.StatusCode = 404;
                    context.Response.OutputStream.Close();
                    return;
                }

                _runtime.DispatchRequest(_callback, context, requestBody, completion);
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

        private static async Task<string> ReadRequestBodyAsync(
            HttpListenerRequest request,
            CancellationToken cancellationToken)
        {
            if (!request.HasEntityBody)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(
                request.InputStream,
                request.ContentEncoding ?? Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ResponseHost
    {
        private readonly HttpListenerResponse _response;
        private JsObject? _responseObject;

        public ResponseHost(HttpListenerResponse response)
        {
            _response = response;
            _response.StatusCode = 200;
        }

        public bool HasEnded { get; private set; }

        public JsObject CreateResponseObject(MiniNodeRuntime runtime)
        {
            var response = new JsObject();
            _responseObject = response;
            SetProperty(response, "statusCode", _response.StatusCode);
            SetProperty(response, "finished", false);

            SetProperty(response, "setHeader", runtime.CreateHostFunction(args =>
            {
                var key = GetRequiredString(args, 0, "res.setHeader");
                var value = args.Count > 1 ? ToHostString(args[1]) : string.Empty;
                SetHeader(key, value);
                return JsValue.Undefined;
            }));

            SetProperty(response, "getHeader", runtime.CreateHostFunction(args =>
            {
                var key = GetRequiredString(args, 0, "res.getHeader");
                return string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase)
                    ? _response.ContentType ?? string.Empty
                    : _response.Headers[key] ?? string.Empty;
            }));

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
                if (_responseObject is not null)
                {
                    SetProperty(_responseObject, "statusCode", _response.StatusCode);
                }
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
            ApplyResponseProperties();
            var bytes = Encoding.UTF8.GetBytes(text);
            _response.ContentLength64 = bytes.Length;
            _response.OutputStream.Write(bytes, 0, bytes.Length);
            _response.OutputStream.Close();
            HasEnded = true;
            if (_responseObject is not null)
            {
                SetProperty(_responseObject, "finished", true);
            }
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

        private void ApplyResponseProperties()
        {
            if (_responseObject is null ||
                !_responseObject.TryGetProperty("statusCode", out var statusCodeValue) ||
                !statusCodeValue.TryGetDouble(out var statusCode))
            {
                return;
            }

            _response.StatusCode = (int)statusCode;
        }
    }
}
