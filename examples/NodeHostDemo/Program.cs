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
    private readonly Queue<ScheduledCallback> _nextTickQueue = [];
    private readonly HashSet<int> _clearedImmediateHandles = [];
    private readonly object _engineExecutionLock = new();
    private readonly IJsCallable _arrayFactory;
    private int _pendingScheduledCallbackCount;
    private int _nextImmediateHandle;

    public MiniNodeRuntime(string scriptPath)
    {
        _scriptPath = scriptPath;
        _scriptDirectory = Path.GetDirectoryName(scriptPath) ?? Directory.GetCurrentDirectory();
        _arrayFactory = CreateArrayFactory();
        _engine.SetGlobalFunction("require", Require);
        _engine.SetGlobalValue("process", CreateProcessObject());
        _engine.SetGlobalValue("console", CreateConsoleObject());
        _engine.SetGlobalFunction("setImmediate", ScheduleImmediate);
        _engine.SetGlobalFunction("clearImmediate", ClearImmediate);
        InstallGlobalBuffer();
        InstallTextEncodingGlobals();
        InstallNodeCompatibilityShims();
    }

    public bool HasActiveServers => _servers.Count > 0;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        LoadScriptModule(_scriptPath, _scriptDirectory);
        DrainQueuedCallbacks();
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
        var moduleName = NormalizeBuiltInModuleName(GetRequiredString(args, 0, "require"));
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
            "os" => CreateOsModule(),
            "path" => CreatePathModule(),
            "querystring" => CreateQueryStringModule(this),
            "stream" => CreateStreamModule(),
            "string_decoder" => CreateStringDecoderModule(),
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

    private static string NormalizeBuiltInModuleName(string moduleName)
    {
        const string nodePrefix = "node:";
        return moduleName.StartsWith(nodePrefix, StringComparison.Ordinal)
            ? moduleName[nodePrefix.Length..]
            : moduleName;
    }

    private JsObject CreateProcessObject()
    {
        var process = new JsObject();
        SetProperty(process, "cwd", CreateHostFunction(_ => _scriptDirectory));
        SetProperty(process, "argv", CreateJsArray([]));
        SetProperty(process, "env", new JsObject());
        SetProperty(process, "noDeprecation", false);
        SetProperty(process, "platform", GetNodePlatform());
        SetProperty(process, "traceDeprecation", false);
        SetProperty(process, "stderr", CreateStderrObject());
        SetProperty(process, "stdout", CreateStdoutObject());
        SetProperty(process, "nextTick", CreateHostFunction(ScheduleNextTick));
        SetProperty(process, "uptime", CreateHostFunction(_ =>
        {
            var elapsed = DateTimeOffset.UtcNow - _startedAt;
            return JsValue.FromDouble(elapsed.TotalSeconds);
        }));
        SetProperty(process, "hrtime", CreateHostFunction(args =>
        {
            var elapsed = DateTimeOffset.UtcNow - _startedAt;
            var seconds = (long)elapsed.TotalSeconds;
            var nanoseconds = (long)((elapsed - TimeSpan.FromSeconds(seconds)).Ticks * 100);

            if (args.Count > 0 &&
                args[0].TryGetObject<JsObject>(out var previous))
            {
                var previousSeconds = ReadNumberProperty(previous, "0");
                var previousNanoseconds = ReadNumberProperty(previous, "1");
                seconds -= (long)previousSeconds;
                nanoseconds -= (long)previousNanoseconds;
                if (nanoseconds < 0)
                {
                    seconds--;
                    nanoseconds += 1_000_000_000;
                }
            }

            return CreateJsArray([(JsValue)seconds, (JsValue)nanoseconds]);
        }));
        return process;
    }

    private static double ReadNumberProperty(JsObject obj, string propertyName)
    {
        return obj.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var number)
            ? number
            : 0;
    }

    private static string GetNodePlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return "win32";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "darwin";
        }

        return "linux";
    }

    private JsObject CreateConsoleObject()
    {
        var console = new JsObject();
        SetProperty(console, "log", CreateHostFunction(args =>
        {
            Console.Out.WriteLine(FormatConsoleArguments(args));
            return JsValue.Undefined;
        }));
        SetProperty(console, "info", CreateHostFunction(args =>
        {
            Console.Out.WriteLine(FormatConsoleArguments(args));
            return JsValue.Undefined;
        }));
        SetProperty(console, "warn", CreateHostFunction(args =>
        {
            Console.Error.WriteLine(FormatConsoleArguments(args));
            return JsValue.Undefined;
        }));
        SetProperty(console, "error", CreateHostFunction(args =>
        {
            Console.Error.WriteLine(FormatConsoleArguments(args));
            return JsValue.Undefined;
        }));
        return console;
    }

    private static string FormatConsoleArguments(IReadOnlyList<JsValue> args)
    {
        if (args.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < args.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(ToHostString(args[i]));
        }

        return builder.ToString();
    }

    private JsValue ScheduleNextTick(IReadOnlyList<JsValue> args)
    {
        EnqueueCallback(_nextTickQueue, handle: 0, args);
        return JsValue.Undefined;
    }

    private JsValue ScheduleImmediate(IReadOnlyList<JsValue> args)
    {
        if (!TryGetCallable(args, 0, out var callback))
        {
            return JsValue.Undefined;
        }

        var handle = ++_nextImmediateHandle;
        var callbackArgs = CreateCallbackArgs(args);
        ScheduleMacrotask(() =>
        {
            if (!_clearedImmediateHandles.Remove(handle))
            {
                callback.Invoke(callbackArgs, JsValue.Undefined);
            }
        });
        return handle;
    }

    private JsValue ClearImmediate(IReadOnlyList<JsValue> args)
    {
        if (args.Count > 0 && args[0].TryGetDouble(out var handle))
        {
            _clearedImmediateHandles.Add((int)handle);
        }

        return JsValue.Undefined;
    }

    private static void EnqueueCallback(Queue<ScheduledCallback> queue, int handle, IReadOnlyList<JsValue> args)
    {
        if (!TryGetCallable(args, 0, out var callback))
        {
            return;
        }

        queue.Enqueue(new ScheduledCallback(handle, callback, CreateCallbackArgs(args)));
    }

    private static JsValue[] CreateCallbackArgs(IReadOnlyList<JsValue> args)
    {
        var callbackArgs = new JsValue[Math.Max(0, args.Count - 1)];
        for (var i = 1; i < args.Count; i++)
        {
            callbackArgs[i - 1] = args[i];
        }

        return callbackArgs;
    }

    private void DrainQueuedCallbacks()
    {
        const int maxQueuedCallbacksPerTurn = 100_000;
        var executed = 0;

        while (_nextTickQueue.Count > 0)
        {
            if (++executed > maxQueuedCallbacksPerTurn)
            {
                throw new InvalidOperationException("Queued JavaScript callbacks did not drain.");
            }

            var scheduled = _nextTickQueue.Dequeue();

            scheduled.Callback.Invoke(scheduled.Args, JsValue.Undefined);
        }
    }

    private void ScheduleMacrotask(Action callback)
    {
        Interlocked.Increment(ref _pendingScheduledCallbackCount);
        ScheduleEngineTask(() =>
        {
            try
            {
                DrainQueuedCallbacks();
                callback();
                DrainQueuedCallbacks();
            }
            finally
            {
                Interlocked.Decrement(ref _pendingScheduledCallbackCount);
            }
        });
    }

    private void ScheduleEngineTask(Action callback)
    {
        var thread = new Thread(state =>
        {
            var (runtime, queuedCallback) = ((MiniNodeRuntime Runtime, Action Callback))state!;
            lock (runtime._engineExecutionLock)
            {
                queuedCallback();
            }
        }, maxStackSize: 16 * 1024 * 1024)
        {
            IsBackground = true,
            Name = "NodeHostDemo JavaScript callback"
        };
        thread.Start((this, callback));
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

              function toHex(text) {
                var result = '';
                for (var i = 0; i < text.length; i++) {
                  var hex = text.charCodeAt(i).toString(16);
                  result += hex.length === 1 ? '0' + hex : hex.slice(-2);
                }

                return result;
              }

              function toBase64(text) {
                var chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';
                var result = '';
                for (var i = 0; i < text.length; i += 3) {
                  var a = text.charCodeAt(i) & 255;
                  var b = i + 1 < text.length ? text.charCodeAt(i + 1) & 255 : 0;
                  var c = i + 2 < text.length ? text.charCodeAt(i + 2) & 255 : 0;
                  var triplet = (a << 16) | (b << 8) | c;
                  result += chars[(triplet >> 18) & 63];
                  result += chars[(triplet >> 12) & 63];
                  result += i + 1 < text.length ? chars[(triplet >> 6) & 63] : '=';
                  result += i + 2 < text.length ? chars[triplet & 63] : '=';
                }

                return result;
              }

              Buffer.prototype.toString = function (encoding) {
                var text = this._bufferString || '';
                if (encoding === 'hex') {
                  return toHex(text);
                }

                if (encoding === 'base64') {
                  return toBase64(text);
                }

                return text;
              };

              Buffer.prototype.fill = function (value) {
                var text = value === undefined ? '\0' : String(value);
                this._bufferString = new Array(this.length + 1).join(text.charAt(0));
                return this;
              };

              Buffer.prototype.slice = function (start, end) {
                var length = this.length || 0;
                var from = start === undefined ? 0 : Number(start);
                var to = end === undefined ? length : Number(end);

                if (from < 0) {
                  from = Math.max(length + from, 0);
                } else {
                  from = Math.min(from, length);
                }

                if (to < 0) {
                  to = Math.max(length + to, 0);
                } else {
                  to = Math.min(to, length);
                }

                if (to < from) {
                  to = from;
                }

                return Buffer((this._bufferString || '').slice(from, to));
              };

              Buffer.prototype.subarray = Buffer.prototype.slice;

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

    private void InstallTextEncodingGlobals()
    {
        _engine.SetGlobalValue("TextDecoder", CreateTextDecoderConstructor());
        _engine.EvaluateSync("""
            TextEncoder = function TextEncoder() {
              if (!(this instanceof TextEncoder)) {
                return new TextEncoder();
              }

              this.encoding = 'utf-8';
            };

            TextEncoder.prototype.encode = function encode(input) {
              var binary = unescape(encodeURIComponent(String(input === undefined ? '' : input)));
              var bytes = new Uint8Array(binary.length);
              for (var i = 0; i < binary.length; i++) {
                bytes[i] = binary.charCodeAt(i);
              }

              return bytes;
            };
            """);
    }

    private HostFunction CreateTextDecoderConstructor()
    {
        var constructor = new HostFunction((thisValue, args) =>
        {
            var label = args.Count > 0 ? ToHostString(args[0]) : "utf-8";
            var encoding = NormalizeTextEncodingLabel(label);
            var target = thisValue.TryGetObject<JsObject>(out var thisObject) ? thisObject : new JsObject();

            SetProperty(target, "encoding", GetTextEncodingName(encoding));
            SetProperty(target, "decode", CreateHostFunction(decodeArgs =>
            {
                if (decodeArgs.Count == 0 || decodeArgs[0].IsNullOrUndefined)
                {
                    return string.Empty;
                }

                return encoding.GetString(GetByteSourceBytes(decodeArgs[0]));
            }));

            return thisValue.TryGetObject<JsObject>(out _) ? JsValue.Undefined : target;
        })
        {
            Realm = _engine.GlobalObject
        };

        return constructor;
    }

    private static Encoding NormalizeTextEncodingLabel(string label)
    {
        var normalized = label.Trim().ToLowerInvariant();
        return normalized switch
        {
            "" or "utf-8" or "utf8" or "unicode-1-1-utf-8" => Encoding.UTF8,
            "latin1" or "iso-8859-1" => Encoding.Latin1,
            _ => throw new NotSupportedException($"TextDecoder encoding '{label}' is not implemented by this demo host.")
        };
    }

    private static string GetTextEncodingName(Encoding encoding)
    {
        return encoding.CodePage == Encoding.Latin1.CodePage ? "iso-8859-1" : "utf-8";
    }

    private static byte[] GetByteSourceBytes(JsValue value)
    {
        if (value.TryGetObject<JsObject>(out var bufferObject) &&
            bufferObject.TryGetProperty("_bufferString", out var bufferString) &&
            bufferString.TryGetString(out var bufferText))
        {
            return Encoding.Latin1.GetBytes(bufferText);
        }

        if (!value.TryGetObject<IJsPropertyAccessor>(out var arrayLike) ||
            !arrayLike.TryGetProperty("length", out var lengthValue) ||
            !lengthValue.TryGetDouble(out var lengthDouble))
        {
            return [];
        }

        var length = Math.Max(0, (int)lengthDouble);
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            if (!arrayLike.TryGetProperty(i.ToString(CultureInfo.InvariantCulture), out var element) ||
                !element.TryGetDouble(out var byteValue))
            {
                continue;
            }

            bytes[i] = unchecked((byte)(int)byteValue);
        }

        return bytes;
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
                return FormatDigest(bytes, format);
            }));

            return hash;
        }));
        SetProperty(crypto, "createHmac", CreateHostFunction(args =>
        {
            var algorithm = GetRequiredString(args, 0, "crypto.createHmac");
            if (!string.Equals(algorithm, "sha256", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("Only sha256 HMAC is implemented by this demo host.");
            }

            var key = Encoding.UTF8.GetBytes(GetRequiredString(args, 1, "crypto.createHmac"));
            var input = new StringBuilder();
            var hmac = new JsObject();
            SetProperty(hmac, "update", CreateHostFunction((thisValue, updateArgs) =>
            {
                if (updateArgs.Count > 0)
                {
                    input.Append(ToHostString(updateArgs[0]));
                }

                return thisValue.IsUndefined ? (JsValue)hmac : thisValue;
            }));
            SetProperty(hmac, "digest", CreateHostFunction(digestArgs =>
            {
                var format = digestArgs.Count > 0 ? ToHostString(digestArgs[0]) : string.Empty;
                using var hash = new System.Security.Cryptography.HMACSHA256(key);
                var bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(input.ToString()));
                return FormatDigest(bytes, format);
            }));

            return hmac;
        }));
        var randomBytes = CreateHostFunction(args =>
        {
            var size = GetRequiredInt(args, 0, "crypto.randomBytes");
            var buffer = CreateRandomBuffer(size);
            if (TryGetCallable(args, 1, out var callback))
            {
                ScheduleMacrotask(() => callback.Invoke([JsValue.Null, buffer], JsValue.Undefined));
            }

            return buffer;
        });
        SetProperty(crypto, "randomBytes", randomBytes);
        SetProperty(crypto, "pseudoRandomBytes", randomBytes);
        SetProperty(crypto, "timingSafeEqual", CreateHostFunction(args =>
        {
            var left = ToHostString(args.Count > 0 ? args[0] : JsValue.EmptyString);
            var right = ToHostString(args.Count > 1 ? args[1] : JsValue.EmptyString);
            if (left.Length != right.Length)
            {
                throw new ArgumentException("Input buffers must have the same length.");
            }

            var diff = 0;
            for (var i = 0; i < left.Length; i++)
            {
                diff |= left[i] ^ right[i];
            }

            return diff == 0;
        }));
        return crypto;
    }

    private JsValue FormatDigest(byte[] bytes, string format)
    {
        if (string.IsNullOrEmpty(format))
        {
            return CreateBufferFromBytes(bytes);
        }

        return string.Equals(format, "base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToBase64String(bytes)
            : Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private JsValue CreateRandomBuffer(int size)
    {
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Random byte length must be non-negative.");
        }

        var bytes = new byte[size];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return CreateBufferFromBytes(bytes);
    }

    private JsValue CreateBufferFromBytes(byte[] bytes)
    {
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            chars[i] = (char)bytes[i];
        }

        var literal = JsonSerializer.Serialize(new string(chars));
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("Buffer.from(" + literal + ")"));
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

    private JsObject CreateStdoutObject()
    {
        var stdout = new JsObject();
        SetProperty(stdout, "isTTY", false);
        SetProperty(stdout, "write", CreateHostFunction(args =>
        {
            if (args.Count > 0)
            {
                Console.Out.Write(ToHostString(args[0]));
            }

            return true;
        }));
        return stdout;
    }

    private JsObject CreateOsModule()
    {
        var os = new JsObject();
        SetProperty(os, "release", CreateHostFunction(_ => Environment.OSVersion.VersionString));
        return os;
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

    private JsValue CreateStringDecoderModule()
    {
        return JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            (function () {
              function StringDecoder(encoding) {
                this.encoding = encoding || 'utf8';
              }

              StringDecoder.prototype.write = function (buffer) {
                if (buffer === undefined || buffer === null) {
                  return '';
                }

                return typeof buffer.toString === 'function'
                  ? buffer.toString(this.encoding)
                  : String(buffer);
              };

              StringDecoder.prototype.end = function (buffer) {
                return buffer === undefined || buffer === null ? '' : this.write(buffer);
              };

              return { StringDecoder: StringDecoder };
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

        SetProperty(fs, "readFile", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.readFile");
            var callbackIndex = TryGetCallable(args, 1, out var callback)
                ? 1
                : 2;
            if (callbackIndex == 2 && !TryGetCallable(args, callbackIndex, out callback))
            {
                throw new ArgumentException("fs.readFile requires a callback.");
            }

            var resolvedPath = ResolveScriptPath(requestedPath);
            ScheduleMacrotask(() =>
            {
                try
                {
                    callback.Invoke([JsValue.Null, (JsValue)File.ReadAllText(resolvedPath)], JsValue.Undefined);
                }
                catch (FileNotFoundException)
                {
                    callback.Invoke([(JsValue)CreateFileSystemError("ENOENT", requestedPath)], JsValue.Undefined);
                }
            });

            return JsValue.Undefined;
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

        SetProperty(fs, "readdirSync", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.readdirSync");
            var resolvedPath = ResolveScriptPath(requestedPath);
            if (!Directory.Exists(resolvedPath))
            {
                throw new DirectoryNotFoundException($"ENOENT: no such file or directory, scandir '{requestedPath}'");
            }

            var names = Directory
                .EnumerateFileSystemEntries(resolvedPath)
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrEmpty(name))
                .Order(StringComparer.Ordinal)
                .Select(static name => (JsValue)name!)
                .ToArray();
            return CreateJsArray(names);
        }));

        SetProperty(fs, "statSync", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.statSync");
            var resolvedPath = ResolveScriptPath(requestedPath);
            var exists = File.Exists(resolvedPath) || Directory.Exists(resolvedPath);
            if (!exists)
            {
                throw new FileNotFoundException($"ENOENT: no such file or directory, stat '{requestedPath}'", resolvedPath);
            }

            var isFile = File.Exists(resolvedPath);
            var stats = new JsObject();
            SetProperty(stats, "isFile", CreateHostFunction(_ => isFile));
            SetProperty(stats, "isDirectory", CreateHostFunction(_ => !isFile));
            return stats;
        }));

        SetProperty(fs, "stat", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.stat");
            if (!TryGetCallable(args, 1, out var callback))
            {
                throw new ArgumentException("fs.stat requires a callback function.");
            }

            var resolvedPath = ResolveScriptPath(requestedPath);
            if (!File.Exists(resolvedPath) && !Directory.Exists(resolvedPath))
            {
                callback.Invoke([(JsValue)CreateFileSystemError("ENOENT", requestedPath)], JsValue.Undefined);
                return JsValue.Undefined;
            }

            callback.Invoke([JsValue.Null, (JsValue)CreateStatsObject(resolvedPath)], JsValue.Undefined);
            return JsValue.Undefined;
        }));

        SetProperty(fs, "realpathSync", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.realpathSync");
            return ResolveScriptPath(requestedPath);
        }));

        SetProperty(fs, "createReadStream", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "fs.createReadStream");
            var resolvedPath = ResolveScriptPath(requestedPath);
            var stream = new JsObject();

            SetProperty(stream, "on", CreateHostFunction((thisValue, _) => thisValue.IsUndefined ? stream : thisValue));
            SetProperty(stream, "destroy", CreateHostFunction(_ => JsValue.Undefined));
            SetProperty(stream, "pipe", CreateHostFunction((thisValue, pipeArgs) =>
            {
                if (!File.Exists(resolvedPath))
                {
                    return thisValue.IsUndefined ? stream : thisValue;
                }

                var contents = File.ReadAllText(resolvedPath);
                if (pipeArgs.Count > 0 &&
                    pipeArgs[0].TryGetObject<JsObject>(out var target) &&
                    target.TryGetProperty("end", out var endValue) &&
                    endValue.TryGetObject<IJsCallable>(out var end))
                {
                    end.Invoke([(JsValue)contents], pipeArgs[0]);
                }

                return pipeArgs.Count > 0 ? pipeArgs[0] : thisValue.IsUndefined ? stream : thisValue;
            }));

            return stream;
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

    private JsObject CreateStatsObject(string resolvedPath)
    {
        var isFile = File.Exists(resolvedPath);
        var stats = new JsObject();
        SetProperty(stats, "isFile", CreateHostFunction(_ => isFile));
        SetProperty(stats, "isDirectory", CreateHostFunction(_ => !isFile));
        SetProperty(stats, "size", isFile ? new FileInfo(resolvedPath).Length : 0);
        SetProperty(stats, "ino", 0);

        var lastWriteTime = isFile
            ? File.GetLastWriteTimeUtc(resolvedPath)
            : Directory.GetLastWriteTimeUtc(resolvedPath);
        var unixMilliseconds = new DateTimeOffset(lastWriteTime).ToUnixTimeMilliseconds();
        var dateExpression = "new Date(" + unixMilliseconds.ToString(CultureInfo.InvariantCulture) + ")";
        var modified = JsValue.FromObjectUnsafe(_engine.EvaluateSync(dateExpression));
        SetProperty(stats, "mtime", modified);
        SetProperty(stats, "ctime", modified);
        return stats;
    }

    private static JsObject CreateFileSystemError(string code, string requestedPath)
    {
        var error = new JsObject();
        SetProperty(error, "code", code);
        SetProperty(error, "message", $"{code}: no such file or directory, stat '{requestedPath}'");
        SetProperty(error, "path", requestedPath);
        return error;
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

        SetProperty(path, "normalize", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "path.normalize");
            if (requestedPath.Length == 0)
            {
                return ".";
            }

            var normalized = Path.GetFullPath(requestedPath, _scriptDirectory);
            return Path.IsPathRooted(requestedPath)
                ? normalized
                : Path.GetRelativePath(_scriptDirectory, normalized);
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

        SetProperty(path, "isAbsolute", CreateHostFunction(args =>
        {
            var requestedPath = GetRequiredString(args, 0, "path.isAbsolute");
            return Path.IsPathRooted(requestedPath);
        }));

        SetProperty(path, "sep", Path.DirectorySeparatorChar.ToString());

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
        SetProperty(http, "METHODS", JsValue.FromObjectUnsafe(_engine.EvaluateSync("""
            [
              'ACL', 'BIND', 'CHECKOUT', 'CONNECT', 'COPY', 'DELETE', 'GET', 'HEAD',
              'LINK', 'LOCK', 'M-SEARCH', 'MERGE', 'MKACTIVITY', 'MKCALENDAR',
              'MKCOL', 'MOVE', 'NOTIFY', 'OPTIONS', 'PATCH', 'POST', 'PROPFIND',
              'PROPPATCH', 'PURGE', 'PUT', 'REBIND', 'REPORT', 'SEARCH', 'SOURCE',
              'SUBSCRIBE', 'TRACE', 'UNBIND', 'UNLINK', 'UNLOCK', 'UNSUBSCRIBE'
            ]
            """)));
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
        ScheduleEngineTask(() =>
        {
            var response = new ResponseHost(context.Response, completion);
            try
            {
                callback.Invoke(
                    [(JsValue)CreateRequestObject(context.Request, requestBody), (JsValue)response.CreateResponseObject(this)],
                    JsValue.Undefined);

                DrainQueuedCallbacks();
                if (!response.HasEnded &&
                    Interlocked.CompareExchange(ref _pendingScheduledCallbackCount, 0, 0) == 0)
                {
                    response.End(JsValue.EmptyString);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                if (!response.HasEnded)
                {
                    response.SendError(500, ex.Message);
                }

                completion.TrySetException(ex);
            }
        });
    }

    private JsObject CreateRequestObject(HttpListenerRequest request, string body)
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
        SetProperty(req, "complete", true);
        SetProperty(req, "readable", true);
        SetProperty(req, "socket", CreateSocketObject());
        SetProperty(req, "on", CreateRequestOnFunction(req, body));
        SetProperty(req, "once", CreateRequestOnFunction(req, body));
        SetProperty(req, "removeListener", CreateNoOpChainFunction(req));
        SetProperty(req, "pause", CreateNoOpChainFunction(req));
        SetProperty(req, "resume", CreateNoOpChainFunction(req));
        SetProperty(req, "unpipe", CreateNoOpChainFunction(req));
        return req;
    }

    private static JsObject CreateSocketObject()
    {
        var socket = new JsObject();
        SetProperty(socket, "readable", true);
        SetProperty(socket, "writable", true);
        SetProperty(socket, "destroy", new HostFunction(_ => JsValue.Undefined, isConstructor: false));
        SetProperty(socket, "on", CreateNoOpChainFunction(socket));
        SetProperty(socket, "once", CreateNoOpChainFunction(socket));
        SetProperty(socket, "removeListener", CreateNoOpChainFunction(socket));
        return socket;
    }

    private HostFunction CreateRequestOnFunction(JsObject request, string body)
    {
        var bodyDispatched = false;
        var endDispatched = false;

        return CreateHostFunction((thisValue, args) =>
        {
            if (args.Count < 2 ||
                !args[0].TryGetString(out var eventName) ||
                !TryGetCallable(args, 1, out var callback))
            {
                return thisValue.IsUndefined ? (JsValue)request : thisValue;
            }

            switch (eventName)
            {
                case "data" when !bodyDispatched:
                    bodyDispatched = true;
                    ScheduleMacrotask(() =>
                    {
                        if (body.Length > 0)
                        {
                            callback.Invoke([(JsValue)body], request);
                        }
                    });
                    break;
                case "end" when !endDispatched:
                    endDispatched = true;
                    ScheduleMacrotask(() =>
                    {
                        SetProperty(request, "readable", false);
                        callback.Invoke([], request);
                    });
                    break;
            }

            return thisValue.IsUndefined ? (JsValue)request : thisValue;
        });
    }

    private static HostFunction CreateNoOpChainFunction(JsObject fallbackThis)
    {
        return new HostFunction((thisValue, _) => thisValue.IsUndefined ? fallbackThis : thisValue, isConstructor: false);
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

    private IJsCallable CreateArrayFactory()
    {
        var factoryValue = JsValue.FromObjectUnsafe(_engine.EvaluateSync("(function () { return []; })"));
        if (!factoryValue.TryGetCallable(out var callable))
        {
            throw new InvalidOperationException("Could not create JavaScript array factory.");
        }

        return callable;
    }

    private JsValue CreateJsArray(IReadOnlyList<JsValue> values)
    {
        var arrayValue = _arrayFactory.Invoke([], JsValue.Undefined);
        if (!arrayValue.TryGetArray(out var array))
        {
            throw new InvalidOperationException("JavaScript array factory did not return an array.");
        }

        for (var index = 0; index < values.Count; index++)
        {
            array.SetProperty(index.ToString(CultureInfo.InvariantCulture), values[index]);
        }

        return arrayValue;
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

    private sealed class ScheduledCallback(int handle, IJsCallable callback, JsValue[] args)
    {
        public int Handle { get; } = handle;

        public IJsCallable Callback { get; } = callback;

        public JsValue[] Args { get; } = args;
    }

    private sealed class ResponseHost
    {
        private readonly HttpListenerResponse _response;
        private readonly TaskCompletionSource _completion;
        private readonly StringBuilder _body = new();
        private bool _headersStarted;
        private JsObject? _responseObject;

        public ResponseHost(HttpListenerResponse response, TaskCompletionSource completion)
        {
            _response = response;
            _completion = completion;
            _response.StatusCode = 200;
        }

        public bool HasEnded { get; private set; }

        public JsObject CreateResponseObject(MiniNodeRuntime runtime)
        {
            var response = new JsObject();
            _responseObject = response;
            SetProperty(response, "statusCode", _response.StatusCode);
            SetProperty(response, "finished", false);
            SetProperty(response, "headersSent", false);
            SetProperty(response, "_header", JsValue.Undefined);
            SetProperty(response, "socket", CreateSocketObject());
            SetProperty(response, "on", CreateNoOpChainFunction(response));
            SetProperty(response, "once", CreateNoOpChainFunction(response));
            SetProperty(response, "removeListener", CreateNoOpChainFunction(response));
            SetProperty(response, "emit", runtime.CreateHostFunction(_ => false));

            SetProperty(response, "setHeader", runtime.CreateHostFunction(args =>
            {
                var key = GetRequiredString(args, 0, "res.setHeader");
                if (args.Count > 1 && TryGetHeaderString(args[1], out var value))
                {
                    SetHeader(key, value);
                }
                else
                {
                    RemoveHeader(key);
                }

                return JsValue.Undefined;
            }));

            SetProperty(response, "getHeader", runtime.CreateHostFunction(args =>
            {
                var key = GetRequiredString(args, 0, "res.getHeader");
                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrEmpty(_response.ContentType)
                        ? JsValue.Undefined
                        : (JsValue)_response.ContentType;
                }

                var value = _response.Headers[key];
                return value is null
                    ? JsValue.Undefined
                    : (JsValue)value;
            }));

            SetProperty(response, "getHeaderNames", runtime.CreateHostFunction(_ =>
            {
                var names = new List<JsValue>();
                foreach (var key in _response.Headers.AllKeys)
                {
                    if (key is not null)
                    {
                        names.Add(key.ToLowerInvariant());
                    }
                }

                if (!string.IsNullOrEmpty(_response.ContentType))
                {
                    names.Add("content-type");
                }

                return runtime.CreateJsArray(names);
            }));

            SetProperty(response, "removeHeader", runtime.CreateHostFunction(args =>
            {
                var key = GetRequiredString(args, 0, "res.removeHeader");
                RemoveHeader(key);
                return JsValue.Undefined;
            }));

            SetProperty(response, "writeHead", runtime.CreateHostFunction(args =>
            {
                WriteHead(args);
                return JsValue.Undefined;
            }));

            SetProperty(response, "_implicitHeader", runtime.CreateHostFunction(_ =>
            {
                InvokeImplicitHeader();
                return JsValue.Undefined;
            }));

            SetProperty(response, "write", runtime.CreateHostFunction(args =>
            {
                Write(args.Count > 0 ? args[0] : JsValue.EmptyString);
                return true;
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
                MarkHeadersStarted();
                return;
            }

            foreach (var key in headers.Keys)
            {
                if (!headers.TryGetProperty(key, out var value))
                {
                    continue;
                }

                if (TryGetHeaderString(value, out var headerValue))
                {
                    SetHeader(key, headerValue);
                }
                else
                {
                    RemoveHeader(key);
                }
            }

            MarkHeadersStarted();
        }

        public void Write(JsValue body)
        {
            if (HasEnded)
            {
                return;
            }

            InvokeImplicitHeader();
            _body.Append(ToHostString(body));
        }

        public void End(JsValue body)
        {
            if (HasEnded)
            {
                return;
            }

            if (!body.IsNullOrUndefined)
            {
                _body.Append(ToHostString(body));
            }

            ApplyResponseProperties();
            InvokeImplicitHeader();
            var text = _body.ToString();
            var bytes = Encoding.UTF8.GetBytes(text);
            _response.ContentLength64 = bytes.Length;
            _response.OutputStream.Write(bytes, 0, bytes.Length);
            _response.OutputStream.Close();
            HasEnded = true;
            if (_responseObject is not null)
            {
                SetProperty(_responseObject, "finished", true);
                SetProperty(_responseObject, "headersSent", true);
            }

            _completion.TrySetResult();
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

        private void RemoveHeader(string key)
        {
            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                _response.ContentType = null;
                return;
            }

            _response.Headers.Remove(key);
        }

        private static bool TryGetHeaderString(JsValue value, out string headerValue)
        {
            if (value.IsNullOrUndefined)
            {
                headerValue = string.Empty;
                return false;
            }

            if (value.TryGetArray(out var array))
            {
                if (array.Length == 0)
                {
                    headerValue = string.Empty;
                    return false;
                }

                var values = new List<string>();
                for (var index = 0; index < (int)array.Length; index++)
                {
                    var item = array.GetElement(index);
                    if (!item.IsNullOrUndefined)
                    {
                        values.Add(ToHostString(item));
                    }
                }

                headerValue = string.Join(", ", values);
                return values.Count > 0;
            }

            if (value.TryGetObject<JsObject>(out var obj) &&
                obj.TryGetProperty("length", out var lengthValue) &&
                lengthValue.TryGetDouble(out var length))
            {
                if (length == 0)
                {
                    headerValue = string.Empty;
                    return false;
                }

                var values = new List<string>();
                for (var index = 0; index < (int)length; index++)
                {
                    if (obj.TryGetProperty(index.ToString(CultureInfo.InvariantCulture), out var item) &&
                        !item.IsNullOrUndefined)
                    {
                        values.Add(ToHostString(item));
                    }
                }

                headerValue = string.Join(", ", values);
                return values.Count > 0;
            }

            headerValue = ToHostString(value);
            return true;
        }

        private void InvokeImplicitHeader()
        {
            if (_headersStarted)
            {
                return;
            }

            ApplyResponseProperties();

            if (_responseObject is not null &&
                _responseObject.TryGetProperty("writeHead", out var writeHeadValue) &&
                writeHeadValue.TryGetCallable(out var writeHead))
            {
                writeHead.Invoke([(JsValue)_response.StatusCode], _responseObject);
                return;
            }

            WriteHead([(JsValue)_response.StatusCode]);
        }

        private void MarkHeadersStarted()
        {
            _headersStarted = true;

            if (_responseObject is not null)
            {
                SetProperty(_responseObject, "_header", "sent");
                SetProperty(_responseObject, "headersSent", true);
            }
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
