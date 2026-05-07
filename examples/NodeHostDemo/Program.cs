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
        _engine.SetGlobalFunction("setImmediate", InvokeImmediately);
        _engine.SetGlobalFunction("clearImmediate", _ => JsValue.Undefined);
        InstallGlobalBuffer();
        InstallNodeCompatibilityShims();
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

    private void InstallNodeCompatibilityShims()
    {
        _engine.EvaluateSync("""
            global = this;
            process.listeners = process.listeners || function () { return []; };
            process.listenerCount = process.listenerCount || function () { return 0; };
            process.emit = process.emit || function () { return false; };
            Error.stackTraceLimit = Error.stackTraceLimit || 10;
            Error.captureStackTrace = Error.captureStackTrace || function captureStackTrace(target) {
              function site() {
                return {
                  getFileName: function () { return '<jsengine>'; },
                  getLineNumber: function () { return 1; },
                  getColumnNumber: function () { return 1; },
                  isEval: function () { return false; },
                  getEvalOrigin: function () { return ''; },
                  getFunctionName: function () { return null; },
                  getThis: function () { return null; },
                  getTypeName: function () { return null; },
                  getMethodName: function () { return null; },
                  toString: function () { return '<jsengine>:1:1'; }
                };
              }

              var frames = [site(), site(), site(), site()];
              target.stack = typeof Error.prepareStackTrace === 'function'
                ? Error.prepareStackTrace(target, frames)
                : frames;
            };
            """);
    }

    private JsValue Require(IReadOnlyList<JsValue> args)
    {
        return RequireFrom(_scriptDirectory, args);
    }

    private JsValue RequireFrom(string baseDirectory, IReadOnlyList<JsValue> args)
    {
        var moduleName = GetRequiredString(args, 0, "require");
        if (TryCreateBuiltInModule(moduleName, out var builtInModule))
        {
            return builtInModule;
        }

        return LoadScriptModule(moduleName, baseDirectory);
    }

    private bool TryCreateBuiltInModule(string moduleName, out JsValue module)
    {
        if (_moduleCache.TryGetValue(moduleName, out module))
        {
            return true;
        }

        module = moduleName switch
        {
            "async_hooks" => CreateAsyncHooksModule(),
            "buffer" => CreateBufferModule(),
            "crypto" => CreateCryptoModule(),
            "events" => CreateEventsModule(),
            "fs" => CreateFsModule(),
            "http" => CreateHttpModule(),
            "net" => CreateNetModule(),
            "path" => CreatePathModule(),
            "querystring" => CreateQueryStringModule(this),
            "stream" => CreateStreamModule(),
            "tty" => CreateTtyModule(),
            "url" => CreateUrlModule(),
            "util" => CreateUtilModule(),
            "zlib" => CreateZlibModule(),
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
        SetProperty(process, "cwd", CreateHostFunction(_ => _scriptDirectory));
        SetProperty(process, "env", new JsObject());
        SetProperty(process, "noDeprecation", false);
        SetProperty(process, "traceDeprecation", false);
        SetProperty(process, "stderr", CreateStderrObject());
        SetProperty(process, "stdout", CreateStderrObject());
        SetProperty(process, "nextTick", CreateHostFunction(InvokeImmediately));
        SetProperty(process, "uptime", CreateHostFunction(_ =>
        {
            var elapsed = DateTimeOffset.UtcNow - _startedAt;
            return JsValue.FromDouble(elapsed.TotalSeconds);
        }));
        return process;
    }

    private static JsValue InvokeImmediately(IReadOnlyList<JsValue> args)
    {
        if (!TryGetCallable(args, 0, out var callback))
        {
            return JsValue.Undefined;
        }

        var callbackArgs = new JsValue[Math.Max(0, args.Count - 1)];
        for (var i = 1; i < args.Count; i++)
        {
            callbackArgs[i - 1] = args[i];
        }

        callback.Invoke(callbackArgs, JsValue.Undefined);
        return JsValue.Undefined;
    }

    private JsValue CreateAsyncHooksModule()
    {
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function AsyncResource() {
              }

              AsyncResource.prototype.runInAsyncScope = function (fn, thisArg) {
                var args = Array.prototype.slice.call(arguments, 2);
                return fn.apply(thisArg, args);
              };

              return { AsyncResource: AsyncResource };
            })()
            """));
    }

    private JsValue CreateBufferModule()
    {
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function makeBuffer(value) {
                var text = value === undefined || value === null ? '' : String(value);
                return Object.create(Buffer.prototype, {
                  _bufferString: { value: text, writable: true, enumerable: false, configurable: true },
                  length: { value: text.length, writable: true, enumerable: true, configurable: true }
                });
              }

              function Buffer(value) {
                if (typeof value === 'number') {
                  return makeBuffer(new Array(value + 1).join('\0'));
                }

                return makeBuffer(value);
              }

              Buffer.prototype.toString = function () {
                return this._bufferString || '';
              };

              Buffer.prototype.fill = function (value) {
                var text = value === undefined ? '\0' : String(value);
                this._bufferString = new Array(this.length + 1).join(text.charAt(0));
                return this;
              };

              Buffer.from = function (value) {
                return Buffer(value);
              };

              Buffer.alloc = function (size, fill) {
                var buffer = Buffer(size);
                if (fill !== undefined) {
                  buffer.fill(fill);
                }

                return buffer;
              };

              Buffer.allocUnsafe = function (size) {
                return Buffer(size);
              };

              Buffer.allocUnsafeSlow = Buffer.allocUnsafe;

              Buffer.byteLength = function (value) {
                return String(value === undefined || value === null ? '' : value).length;
              };

              Buffer.isBuffer = function (value) {
                return !!(value && typeof value === 'object' && Object.prototype.hasOwnProperty.call(value, '_bufferString'));
              };

              return { Buffer: Buffer, SlowBuffer: Buffer };
            })()
            """));
    }

    private void InstallGlobalBuffer()
    {
        var bufferModule = CreateBufferModule();
        _moduleCache["buffer"] = bufferModule;

        if (bufferModule.TryGetObject<JsObject>(out var moduleObject) &&
            moduleObject.TryGetProperty("Buffer", out var bufferConstructor) &&
            bufferConstructor.TryGetObject<IJsCallable>(out var bufferObject))
        {
            _engine.SetGlobalValue("Buffer", bufferObject);
        }
    }

    private JsObject CreateCryptoModule()
    {
        var crypto = new JsObject();
        SetProperty(crypto, "createHash", CreateHostFunction(args =>
        {
            var algorithm = GetRequiredString(args, 0, "crypto.createHash");
            if (!string.Equals(algorithm, "sha1", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Only sha1 is implemented by this demo host.");
            }

            var input = new StringBuilder();
            var hash = new JsObject();
            SetProperty(hash, "update", CreateHostFunction((thisValue, updateArgs) =>
            {
                if (updateArgs.Count > 0)
                {
                    input.Append(ToHostString(updateArgs[0]));
                }

                return thisValue.IsUndefined ? (JsValue)hash : thisValue;
            }));
            SetProperty(hash, "digest", CreateHostFunction(digestArgs =>
            {
                var format = digestArgs.Count > 0 ? ToHostString(digestArgs[0]) : string.Empty;
                var bytes = System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(input.ToString()));
                return string.Equals(format, "base64", StringComparison.OrdinalIgnoreCase)
                    ? Convert.ToBase64String(bytes)
                    : Convert.ToHexString(bytes).ToLowerInvariant();
            }));

            return hash;
        }));
        return crypto;
    }

    private JsValue CreateEventsModule()
    {
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function EventEmitter() {
                this._events = this._events || {};
              }

              EventEmitter.prototype.on = EventEmitter.prototype.addListener = function (name, listener) {
                (this._events || (this._events = {}))[name] = this._events[name] || [];
                this._events[name].push(listener);
                return this;
              };

              EventEmitter.prototype.once = function (name, listener) {
                var self = this;
                function onceListener() {
                  self.removeListener(name, onceListener);
                  return listener.apply(this, arguments);
                }

                onceListener.listener = listener;
                return this.on(name, onceListener);
              };

              EventEmitter.prototype.removeListener = EventEmitter.prototype.off = function (name, listener) {
                var listeners = (this._events && this._events[name]) || [];
                for (var i = listeners.length - 1; i >= 0; i--) {
                  if (listeners[i] === listener || listeners[i].listener === listener) {
                    listeners.splice(i, 1);
                  }
                }

                return this;
              };

              EventEmitter.prototype.removeAllListeners = function (name) {
                if (!this._events) return this;
                if (name === undefined) {
                  this._events = {};
                } else {
                  this._events[name] = [];
                }

                return this;
              };

              EventEmitter.prototype.listeners = function (name) {
                return ((this._events && this._events[name]) || []).slice();
              };

              EventEmitter.prototype.listenerCount = function (name) {
                return this.listeners(name).length;
              };

              EventEmitter.prototype.emit = function (name) {
                var listeners = this.listeners(name);
                var args = Array.prototype.slice.call(arguments, 1);
                for (var i = 0; i < listeners.length; i++) {
                  listeners[i].apply(this, args);
                }
                return listeners.length > 0;
              };

              return { EventEmitter: EventEmitter };
            })()
            """));
    }

    private JsObject CreateStderrObject()
    {
        var stderr = new JsObject();
        SetProperty(stderr, "isTTY", false);
        SetProperty(stderr, "write", CreateHostFunction(args =>
        {
            if (args.Count > 0)
            {
                Console.Error.Write(ToHostString(args[0]));
            }

            return true;
        }));
        return stderr;
    }

    private JsObject CreateTtyModule()
    {
        var tty = new JsObject();
        SetProperty(tty, "isatty", CreateHostFunction(_ => false));
        return tty;
    }

    private JsValue CreateStreamModule()
    {
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              var EventEmitter = require('events').EventEmitter;

              function Stream() {
                EventEmitter.call(this);
              }

              Stream.prototype = Object.create(EventEmitter.prototype);
              Stream.prototype.constructor = Stream;
              Stream.prototype.pipe = function (dest) { return dest; };
              Stream.prototype.destroy = function () {
                this.destroyed = true;
                return this;
              };

              function Transform() {
                Stream.call(this);
              }

              Transform.prototype = Object.create(Stream.prototype);
              Transform.prototype.constructor = Transform;
              Transform.prototype._destroy = function () {};
              Stream.Transform = Transform;
              Stream.Readable = Stream;
              Stream.Writable = Stream;
              Stream.Duplex = Stream;
              return Stream;
            })()
            """));
    }

    private JsValue CreateUrlModule()
    {
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function Url() {
              }

              function parse(url, parseQueryString) {
                var text = String(url || '');
                var parsed = new Url();
                var hashIndex = text.indexOf('#');
                var withoutHash = hashIndex >= 0 ? text.substring(0, hashIndex) : text;
                var queryIndex = withoutHash.indexOf('?');

                parsed.href = text;
                parsed.path = withoutHash;
                parsed.pathname = queryIndex >= 0 ? withoutHash.substring(0, queryIndex) : withoutHash;

                if (hashIndex >= 0) {
                  parsed.hash = text.substring(hashIndex);
                }

                if (queryIndex >= 0) {
                  parsed.search = withoutHash.substring(queryIndex);
                  parsed.query = withoutHash.substring(queryIndex + 1);
                } else {
                  parsed.search = null;
                  parsed.query = parseQueryString ? {} : null;
                }

                if (parseQueryString && typeof parsed.query === 'string') {
                  var query = {};
                  var parts = parsed.query.length ? parsed.query.split('&') : [];
                  for (var i = 0; i < parts.length; i++) {
                    var pair = parts[i];
                    var equals = pair.indexOf('=');
                    var key = equals >= 0 ? pair.substring(0, equals) : pair;
                    var value = equals >= 0 ? pair.substring(equals + 1) : '';
                    query[decodeURIComponent(key.replace(/\+/g, '%20'))] =
                      decodeURIComponent(value.replace(/\+/g, '%20'));
                  }

                  parsed.query = query;
                }

                return parsed;
              }

              function format(url) {
                if (typeof url === 'string') return url;
                var pathname = url.pathname || '';
                var search = url.search;
                if (!search && url.query) {
                  if (typeof url.query === 'string') {
                    search = url.query.length ? '?' + url.query : '';
                  } else {
                    var pairs = [];
                    for (var key in url.query) {
                      pairs.push(encodeURIComponent(key) + '=' + encodeURIComponent(url.query[key]));
                    }

                    search = pairs.length ? '?' + pairs.join('&') : '';
                  }
                }

                return pathname + (search || '') + (url.hash || '');
              }

              return { Url: Url, parse: parse, format: format };
            })()
            """));
    }

    private JsValue CreateZlibModule()
    {
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function ZlibStream() {
              }

              ZlibStream.prototype.destroy = function () { this.destroyed = true; };
              ZlibStream.prototype.close = function () {};
              return {
                Gzip: ZlibStream,
                Gunzip: ZlibStream,
                Deflate: ZlibStream,
                DeflateRaw: ZlibStream,
                Inflate: ZlibStream,
                InflateRaw: ZlibStream,
                Unzip: ZlibStream
              };
            })()
            """));
    }

    private JsValue CreateUtilModule()
    {
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function stringify(value) {
                if (typeof value === 'string') return value;
                if (value === null) return 'null';
                if (value === undefined) return 'undefined';
                try { return JSON.stringify(value); } catch (_) { return String(value); }
              }

              return {
                deprecate: function (fn) { return fn; },
                format: function (format) {
                  var index = 1;
                  var values = arguments;
                  var text = String(format).replace(/%[sdijoO%]/g, function (token) {
                    if (token === '%%') return '%';
                    if (index >= values.length) return token;
                    return stringify(values[index++]);
                  });

                  while (index < values.length) {
                    text += ' ' + stringify(values[index++]);
                  }

                  return text;
                },
                inspect: function (value) { return stringify(value); },
                inherits: function (ctor, superCtor) {
                  ctor.super_ = superCtor;
                  ctor.prototype = Object.create(superCtor.prototype, {
                    constructor: {
                      value: ctor,
                      enumerable: false,
                      writable: true,
                      configurable: true
                    }
                  });
                }
              };
            })()
            """));
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

        SetProperty(fs, "ReadStream", JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function ReadStream() {
              }

              ReadStream.prototype.destroy = function () {};
              ReadStream.prototype.close = function () {};
              return ReadStream;
            })()
            """)));

        return fs;
    }

    private JsObject CreateNetModule()
    {
        var net = new JsObject();
        SetProperty(net, "isIP", CreateHostFunction(args =>
        {
            if (args.Count == 0)
            {
                return JsValue.FromDouble(0);
            }

            return IPAddress.TryParse(ToHostString(args[0]), out var address)
                ? JsValue.FromDouble(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 4 : 6)
                : JsValue.FromDouble(0);
        }));
        return net;
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

        SetProperty(path, "relative", CreateHostFunction(args =>
        {
            var from = GetRequiredString(args, 0, "path.relative");
            var to = GetRequiredString(args, 1, "path.relative");
            return Path.GetRelativePath(from, to);
        }));

        SetProperty(path, "resolve", CreateHostFunction(args =>
        {
            var resolvedPath = _scriptDirectory;
            foreach (var arg in args)
            {
                var part = ToHostString(arg);
                if (part.Length == 0)
                {
                    continue;
                }

                resolvedPath = Path.IsPathRooted(part)
                    ? part
                    : Path.Combine(resolvedPath, part);
            }

            return Path.GetFullPath(resolvedPath);
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
        SetProperty(http, "IncomingMessage", JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function IncomingMessage() {
              }

              return IncomingMessage;
            })()
            """)));
        SetProperty(http, "ServerResponse", JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function ServerResponse() {
              }

              return ServerResponse;
            })()
            """)));
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

    private JsValue LoadScriptModule(string moduleName, string baseDirectory)
    {
        var modulePath = ResolveModulePath(moduleName, baseDirectory);
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

        if (string.Equals(Path.GetExtension(modulePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            var json = File.ReadAllText(modulePath);
            var parsedJson = JsValue.FromObjectUnsafe(_engine.EvaluateSync(
                "JSON.parse(" + JsonSerializer.Serialize(json) + ")"));
            _moduleCache[modulePath] = parsedJson;
            return parsedJson;
        }

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
            try
            {
                factory.Invoke(
                    [
                        (JsValue)exports,
                        (JsValue)CreateHostFunction(args => RequireFrom(moduleDirectory, args)),
                        (JsValue)module,
                        (JsValue)modulePath,
                        (JsValue)moduleDirectory
                    ],
                    JsValue.Undefined);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed while loading module '{modulePath}'.", ex);
            }
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

    private static string ResolveModulePath(string requestedPath, string baseDirectory)
    {
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
            var (packageName, packageSubpath) = SplitPackageSpecifier(moduleName);
            var modulePath = Path.Combine(current.FullName, "node_modules", moduleName);
            if (Directory.Exists(modulePath) || File.Exists(modulePath))
            {
                return ResolveFileOrDirectoryModulePath(moduleName, modulePath);
            }

            if (packageSubpath.Length > 0)
            {
                var packagePath = Path.Combine(current.FullName, "node_modules", packageName);
                if (Directory.Exists(packagePath))
                {
                    var subpath = Path.Combine(packagePath, packageSubpath);
                    return ResolveFileOrDirectoryModulePath(moduleName, subpath);
                }
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Cannot find module '{moduleName}'.", moduleName);
    }

    private static (string PackageName, string PackageSubpath) SplitPackageSpecifier(string moduleName)
    {
        var separatorIndex = moduleName.IndexOf('/');
        if (separatorIndex < 0)
        {
            return (moduleName, string.Empty);
        }

        if (moduleName.StartsWith("@", StringComparison.Ordinal))
        {
            var scopedSeparatorIndex = moduleName.IndexOf('/', separatorIndex + 1);
            return scopedSeparatorIndex < 0
                ? (moduleName, string.Empty)
                : (moduleName[..scopedSeparatorIndex], moduleName[(scopedSeparatorIndex + 1)..]);
        }

        return (moduleName[..separatorIndex], moduleName[(separatorIndex + 1)..]);
    }

    private static string ResolveFileOrDirectoryModulePath(string requestedPath, string resolvedPath)
    {
        if (File.Exists(resolvedPath))
        {
            return resolvedPath;
        }

        foreach (var extension in new[] { ".js", ".json" })
        {
            var extensionPath = resolvedPath + extension;
            if (File.Exists(extensionPath))
            {
                return extensionPath;
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

        if (value.TryGetObject<JsObject>(out var obj) &&
            obj.TryGetProperty("_bufferString", out var bufferString) &&
            bufferString.TryGetString(out var bufferText))
        {
            return bufferText;
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
