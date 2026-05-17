using System.Globalization;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AvaloniaSvgBrowserDemo;

internal static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        if (args.Length > 0 && string.Equals(args[0], "--presentation-smoke", StringComparison.Ordinal))
        {
            RunPresentationSmoke();
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], "--presentation-mask-smoke", StringComparison.Ordinal))
        {
            RunPresentationMaskSmoke();
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], "--presentation-sidecar-smoke", StringComparison.Ordinal))
        {
            RunPresentationSidecarSmoke();
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], "--presentation-preload-smoke", StringComparison.Ordinal))
        {
            BuildAvaloniaApp().SetupWithoutStarting();
            RunPresentationPreloadSmoke();
            return;
        }

        if (args.Length > 0 && string.Equals(args[0], "--presentation-live-demo-smoke", StringComparison.Ordinal))
        {
            await RunPresentationLiveDemoSmoke().ConfigureAwait(false);
            return;
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect();
    }

    private static void RunPresentationSmoke()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var deck = PresentationDeck.Load(baseDirectory, "1");
        var document = new SvgSlideDocument(
            deck.CurrentPath,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        var statusUpdates = new List<int>();
        using var host = new SlideScriptHost(
            document,
            deck,
            (loadedDeck, _) => statusUpdates.Add(loadedDeck.CurrentIndex));
        host.Run(Path.Combine(baseDirectory, "scripts", "presentation.js"));

        var startingIndex = deck.CurrentIndex;
        host.DispatchKey("ArrowRight");
        DispatchSmokeFrames(host);
        var expectedNextIndex = (startingIndex + 1) % deck.Count;
        if (deck.CurrentIndex != expectedNextIndex)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"ArrowRight navigated to {deck.CurrentIndex}, expected {expectedNextIndex}."));
        }

        host.DispatchKey("ArrowLeft");
        DispatchSmokeFrames(host);
        if (deck.CurrentIndex != startingIndex)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"ArrowLeft navigated to {deck.CurrentIndex}, expected {startingIndex}."));
        }

        if (statusUpdates.Count < 3 ||
            statusUpdates[0] != startingIndex ||
            statusUpdates[^2] != expectedNextIndex ||
            statusUpdates[^1] != startingIndex)
        {
            throw new InvalidOperationException(
                "Expected presentation status updates to follow JS-driven navigation.");
        }
    }

    private static void DispatchSmokeFrames(SlideScriptHost host, int durationMilliseconds = 640)
    {
        for (var time = 0; time <= durationMilliseconds; time += 16)
        {
            host.DispatchFrame(time);
        }
    }

    private static void RunPresentationMaskSmoke()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var deck = PresentationDeck.Load(baseDirectory, "27");
        var path = deck.Load("ecma-262-94000-unit-tests.svg", 26);
        var document = new SvgSlideDocument(
            path,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        var analysis = document.AnalyzeGeneratedImageMasks();
        if (analysis.GroupCount < 2 || !analysis.HasTransparentPixels || !analysis.HasOpaquePixels)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Expected page 27 to contain two usable image masks, got {analysis}."));
        }
    }

    private static void RunPresentationSidecarSmoke()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var astDeck = PresentationDeck.Load(baseDirectory, "14");
        astDeck.Load("ast-walking-evaluation.svg", 13);
        var astDocument = new SvgSlideDocument(
            astDeck.CurrentPath,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        using var astHost = new SlideScriptHost(astDocument, astDeck);
        astHost.Run(Path.Combine(baseDirectory, "scripts", "presentation.js"));

        if (!astDocument.ContainsElement("ast-generated-bg") ||
            !astDocument.ContainsElement("ast-node-8a") ||
            !string.Equals(astDocument.GetElementText("ast-step-counter"), "1 / 12", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected AST walking sidecar to generate its initial trace view.");
        }

        astHost.DispatchKey("Space");
        if (!string.Equals(astDocument.GetElementText("ast-step-counter"), "2 / 12", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Expected Space to advance the AST walking trace.");
        }

        var redesignDeck = PresentationDeck.Load(baseDirectory, "16");
        redesignDeck.Load("parser-ast-runtime-redesign-map.svg", 15);
        var redesignDocument = new SvgSlideDocument(
            redesignDeck.CurrentPath,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        using var redesignHost = new SlideScriptHost(redesignDocument, redesignDeck);
        redesignHost.Run(Path.Combine(baseDirectory, "scripts", "presentation.js"));
        DispatchSmokeFrames(redesignHost, 5_600);

        if (!redesignDocument.ContainsElement("slide16-improved-word-0") ||
            !redesignDocument.ContainsElement("slide16-circle") ||
            redesignDocument.ContainsElement("slide16-cover-top"))
        {
            throw new InvalidOperationException(
                "Expected page 16 sidecar to own the animated text without static cover patches.");
        }

        var deck = PresentationDeck.Load(baseDirectory, "27");
        deck.Load("ecma-262-94000-unit-tests.svg", 26);
        var document = new SvgSlideDocument(
            deck.CurrentPath,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        using var host = new SlideScriptHost(document, deck);
        host.Run(Path.Combine(baseDirectory, "scripts", "presentation.js"));
        DispatchSmokeFrames(host);

        if (!document.ContainsElement("ecma-confetti-l-00"))
        {
            throw new InvalidOperationException("Expected ECMA slide sidecar to create its overlay elements.");
        }

        deck.Load("test262-goal-explosion.svg", 27);
        document = new SvgSlideDocument(
            deck.CurrentPath,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        using var goalHost = new SlideScriptHost(document, deck);
        goalHost.Run(Path.Combine(baseDirectory, "scripts", "presentation.js"));
        DispatchSmokeFrames(goalHost, 15_000);

        if (!document.ContainsElement("goal-generated-bg") ||
            !document.ContainsElement("goal-node-719") ||
            !document.ContainsElement("goal-force-719"))
        {
            throw new InvalidOperationException("Expected Test262 goal sidecar to rebuild the slide from SVG elements.");
        }

        var greenTests = document.CountElementsWithAttributePrefix("goal-node-", "fill", "#22c55e");
        var redTests = document.CountElementsWithAttributePrefix("goal-node-", "fill", "#ef4444");
        var grayTests = document.CountElementsWithAttributePrefix("goal-node-", "fill", "#9ca3af");
        if (greenTests < 660 || redTests is < 10 or > 45 || grayTests != 0)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Expected mostly green tests with a few red failures left, got green={greenTests}, red={redTests}, gray={grayTests}."));
        }

        deck.Load("jsengine-presentation-reveal.svg", 65);
        document = new SvgSlideDocument(
            deck.CurrentPath,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        using var revealHost = new SlideScriptHost(document, deck);
        revealHost.Run(Path.Combine(baseDirectory, "scripts", "presentation.js"));
        DispatchSmokeFrames(revealHost);

        if (!document.ContainsElement("reveal-face-19") ||
            !document.ContainsElement("reveal-edge-19") ||
            document.CountElementsWithAttributePrefix("reveal-face-", "points", "0,0 1,0 0,1") != 0)
        {
            throw new InvalidOperationException("Expected reveal sidecar to render JS-computed 3D mesh polygons.");
        }
    }

    private static void RunPresentationPreloadSmoke()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var deck = PresentationDeck.Load(baseDirectory, "1");
        var document = new SvgSlideDocument(
            deck.CurrentPath,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        var stopwatch = Stopwatch.StartNew();
        var assetCount = document.Preload(deck.SlidePaths);
        if (assetCount < 100)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Expected to preload the presentation image assets, got {assetCount}."));
        }

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Preloaded {assetCount} presentation image assets in {stopwatch.ElapsedMilliseconds} ms."));
    }

    private static async Task RunPresentationLiveDemoSmoke()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var deck = PresentationDeck.Load(baseDirectory, "63");
        deck.Load("express-live-demo.svg", 62);
        var document = new SvgSlideDocument(
            deck.CurrentPath,
            Path.Combine(baseDirectory, "rendered-slide.svg"),
            static () => { });
        using var host = new SlideScriptHost(document, deck);
        host.Run(Path.Combine(baseDirectory, "scripts", "presentation.js"));

        host.DispatchClick(100, 145);
        host.DispatchClick(310, 145);

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromSeconds(45))
        {
            host.DispatchFrame(stopwatch.Elapsed.TotalMilliseconds);
            var curlText = GetTerminalText(document, "express-demo-curl-line-", 13);
            if (string.Equals(document.GetElementText("express-demo-state"), "upstream app ready on localhost:3000", StringComparison.Ordinal) &&
                curlText.Contains("TJ", StringComparison.Ordinal) &&
                curlText.Contains("[curl exited 0]", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        var diagnostic = string.Create(
            CultureInfo.InvariantCulture,
            $"status={document.GetElementText("express-demo-state")}{Environment.NewLine}" +
            $"host:{Environment.NewLine}{GetTerminalText(document, "express-demo-host-line-", 13)}" +
            $"curl:{Environment.NewLine}{GetTerminalText(document, "express-demo-curl-line-", 13)}");
        throw new InvalidOperationException(
            "Expected the live Express demo slide to start the upstream MVC app and complete curl /users." +
            Environment.NewLine +
            diagnostic);
    }

    private static string GetTerminalText(SvgSlideDocument document, string idPrefix, int lineCount)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < lineCount; index++)
        {
            builder.AppendLine(document.GetElementText(idPrefix + index));
        }

        return builder.ToString();
    }
}

internal sealed class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new DemoWindow(desktop.Args ?? []);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed partial class DemoWindow : Window
{
    private readonly SvgSlideView _svgView;
    private readonly SvgSlideDocument _document;
    private readonly SlideScriptHost _scriptHost;
    private readonly DispatcherTimer _timer;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private TextBlock? _statusText;

    public DemoWindow(IReadOnlyList<string> args)
    {
        _svgView = new SvgSlideView
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Title = "JsEngine Avalonia SVG Browser";
        Width = 1280;
        Height = 720;
        MinWidth = 960;
        MinHeight = 540;
        Background = Brushes.Black;
        CanResize = true;

        var baseDirectory = AppContext.BaseDirectory;
        var launch = ResolveLaunch(baseDirectory, args);
        var renderedPath = Path.Combine(baseDirectory, "rendered-slide.svg");

        _document = new SvgSlideDocument(launch.SvgPath, renderedPath, RenderSvg);
        _svgView.Document = _document;
        PreloadDeckAssets(launch);
        _document.Flush();

        Content = CreateLayout(launch);

        _scriptHost = new SlideScriptHost(_document, launch.Deck, UpdatePresentationStatus);
        if (launch.ScriptPath is not null)
        {
            _scriptHost.Run(launch.ScriptPath);
        }

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _timer.Tick += (_, _) => TickFrame();
        _timer.Start();

        AddHandler(
            KeyDownEvent,
            (_, eventArgs) => DispatchKey(eventArgs),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        AddHandler(
            PointerPressedEvent,
            (_, eventArgs) => DispatchPointer(eventArgs),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        Closed += (_, _) => _scriptHost.Dispose();
    }

    private void PreloadDeckAssets(LaunchOptions launch)
    {
        if (launch.Deck is null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var assetCount = _document.Preload(launch.Deck.SlidePaths);
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Preloaded {assetCount} presentation image assets in {stopwatch.ElapsedMilliseconds} ms."));
    }

    private Control CreateLayout(LaunchOptions launch)
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            }
        };

        root.Children.Add(_svgView);

        _statusText = new TextBlock
        {
            Text = launch.StatusText,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(210, 5, 7, 13)),
            Padding = new Thickness(14, 8),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(_statusText, 1);
        root.Children.Add(_statusText);

        return root;
    }

    private void UpdatePresentationStatus(PresentationDeck deck, string path)
    {
        var statusText = string.Create(
            CultureInfo.InvariantCulture,
            $"Rendering presentation page {deck.CurrentPageNumber}/{deck.DisplayCount}: {Path.GetFileName(path)}. Use Left/Right arrows.");
        Dispatcher.UIThread.Post(() =>
        {
            if (_statusText is not null)
            {
                _statusText.Text = statusText;
            }

            Title = string.Create(
                CultureInfo.InvariantCulture,
                $"JsEngine Avalonia SVG Browser - {deck.CurrentPageNumber}/{deck.DisplayCount}");
        });
    }

    private static LaunchOptions ResolveLaunch(string baseDirectory, IReadOnlyList<string> args)
    {
        if (args.Count > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            if (string.Equals(args[0], "--presentation", StringComparison.Ordinal))
            {
                var pageNumber = args.Count > 1
                    ? args[1]
                    : "2";
                var presentationDeck = PresentationDeck.Load(baseDirectory, pageNumber);
                return new LaunchOptions(
                    presentationDeck.CurrentPath,
                    Path.Combine(baseDirectory, "scripts", "presentation.js"),
                    presentationDeck,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Rendering presentation page {presentationDeck.CurrentPageNumber}/{presentationDeck.Count}. Use Left/Right arrows."));
            }

            var svgPath = Path.GetFullPath(args[0]);
            return new LaunchOptions(
                svgPath,
                null,
                null,
                string.Create(CultureInfo.InvariantCulture, $"Rendering {Path.GetFileName(svgPath)}."));
        }

        var defaultDeck = PresentationDeck.Load(baseDirectory, "1");
        return new LaunchOptions(
            defaultDeck.CurrentPath,
            Path.Combine(baseDirectory, "scripts", "presentation.js"),
            defaultDeck,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Rendering presentation page {defaultDeck.CurrentPageNumber}/{defaultDeck.Count}. Use Left/Right arrows."));
    }

    private sealed record LaunchOptions(
        string SvgPath,
        string? ScriptPath,
        PresentationDeck? Deck,
        string StatusText);
}

internal sealed class PresentationDeck
{
    private readonly Dictionary<string, string> _slidePathsByName = new(StringComparer.Ordinal);
    private readonly string[] _slidePaths;
    private string _currentPath;

    private PresentationDeck(string[] slidePaths, int currentIndex)
    {
        _slidePaths = slidePaths;
        CurrentIndex = currentIndex;
        DisplayCount = slidePaths.Length;
        _currentPath = GetPath(currentIndex);
        foreach (var slidePath in slidePaths)
        {
            _slidePathsByName[Path.GetFileName(slidePath)] = slidePath;
        }
    }

    public int Count => _slidePaths.Length;

    public int DisplayCount { get; private set; }

    public IReadOnlyList<string> SlidePaths => _slidePaths;

    public int CurrentIndex { get; private set; }

    public int CurrentPageNumber => CurrentIndex + 1;

    public string CurrentPath => _currentPath;

    public static PresentationDeck Load(string baseDirectory, string requestedPageNumber)
    {
        var pagesDirectory = Path.Combine(
            baseDirectory,
            "assets",
            "beyond-the-vibe",
            "pages");

        var slidePaths = Directory
            .EnumerateFiles(pagesDirectory, "*.svg")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (slidePaths.Length == 0)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"No presentation SVG pages found in {pagesDirectory}."));
        }

        var currentIndex = 1;
        if (int.TryParse(requestedPageNumber, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPage))
        {
            currentIndex = Math.Clamp(parsedPage - 1, 0, slidePaths.Length - 1);
        }

        return new PresentationDeck(slidePaths, currentIndex);
    }

    public string GetPath(int index)
    {
        if (index < 0 || index >= _slidePaths.Length)
        {
            return string.Empty;
        }

        return _slidePaths[index];
    }

    public string Load(int index)
    {
        if (index < 0 || index >= _slidePaths.Length)
        {
            return CurrentPath;
        }

        CurrentIndex = index;
        _currentPath = GetPath(index);
        return CurrentPath;
    }

    public string Load(string fileName, int displayIndex)
    {
        return Load(fileName, displayIndex, Count);
    }

    public string Load(string fileName, int displayIndex, int displayCount)
    {
        var normalizedFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(normalizedFileName) ||
            !_slidePathsByName.TryGetValue(normalizedFileName, out var path))
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Presentation slide not found: {fileName}"));
        }

        CurrentIndex = Math.Clamp(displayIndex, 0, _slidePaths.Length - 1);
        DisplayCount = Math.Max(1, displayCount);
        _currentPath = path;
        return path;
    }
}

internal sealed partial class DemoWindow
{
    private void TickFrame()
    {
        var elapsed = DateTimeOffset.UtcNow - _startedAt;
        _scriptHost.DispatchFrame(elapsed.TotalMilliseconds);
    }

    private void RenderSvg()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _svgView.InvalidateVisual();
        });
    }

    private void DispatchKey(KeyEventArgs eventArgs)
    {
        var key = NormalizeKey(eventArgs);
        if (key.Length == 0)
        {
            return;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Avalonia key: {key}"));
        _scriptHost.DispatchKey(key);
        eventArgs.Handled = true;
    }

    private void DispatchPointer(PointerPressedEventArgs eventArgs)
    {
        var point = eventArgs.GetPosition(_svgView);
        if (!_document.TryMapToSvgPoint(point, _svgView.Bounds, out var x, out var y))
        {
            return;
        }

        _scriptHost.DispatchClick(x, y);
        eventArgs.Handled = true;
    }

    private static string NormalizeKey(KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Space)
        {
            return "Space";
        }

        if (eventArgs.Key == Key.Left)
        {
            return "ArrowLeft";
        }

        if (eventArgs.Key == Key.Right)
        {
            return "ArrowRight";
        }

        var keyText = eventArgs.Key.ToString();
        return keyText.Length == 1
            ? keyText.ToUpperInvariant()
            : keyText;
    }
}

internal sealed class SvgSlideView : Control
{
    public SvgSlideDocument? Document { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        Document?.Render(context, Bounds);
    }
}

internal sealed class SlideScriptHost : IDisposable
{
    private readonly JsEngine _engine = new();
    private readonly List<IJsCallable> _frameCallbacks = [];
    private readonly List<IJsCallable> _clickCallbacks = [];
    private readonly Dictionary<string, List<IJsCallable>> _keyCallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SvgSlideDocument _document;
    private readonly PresentationDeck? _deck;
    private readonly Action<PresentationDeck, string>? _presentationLoaded;
    private readonly ExpressDemoProcessHost _demoProcessHost = new(AppContext.BaseDirectory);

    public SlideScriptHost(
        SvgSlideDocument document,
        PresentationDeck? deck,
        Action<PresentationDeck, string>? presentationLoaded = null)
    {
        _document = document;
        _deck = deck;
        _presentationLoaded = presentationLoaded;
        _engine.SetGlobalValue("console", CreateConsoleObject());
        _engine.SetGlobalValue("svg", CreateSvgObject());
        _engine.SetGlobalValue("slide", CreateSlideObject());
        _engine.SetGlobalValue("presentation", CreatePresentationObject());
        _engine.SetGlobalValue("demo", CreateDemoObject());
    }

    public void Run(string scriptPath)
    {
        var script = BuildScriptBundle(scriptPath);
        _engine.EvaluateSync(script);
        _document.Flush();
    }

    private static string BuildScriptBundle(string scriptPath)
    {
        var scriptDirectory = Path.GetDirectoryName(scriptPath) ?? AppContext.BaseDirectory;
        var builder = new StringBuilder();
        builder.AppendLine(File.ReadAllText(scriptPath));

        var sidecarDirectory = Path.Combine(scriptDirectory, "slides");
        if (Directory.Exists(sidecarDirectory))
        {
            foreach (var sidecarPath in Directory.EnumerateFiles(sidecarDirectory, "*.js").Order(StringComparer.Ordinal))
            {
                builder.AppendLine();
                builder.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"// sidecar: {Path.GetFileName(sidecarPath)}"));
                builder.AppendLine("(function () {");
                builder.AppendLine(File.ReadAllText(sidecarPath));
                builder.AppendLine("})();");
            }
        }

        builder.AppendLine();
        builder.AppendLine("startPresentation();");
        return builder.ToString();
    }

    public void DispatchFrame(double milliseconds)
    {
        var timeValue = new JsValue(milliseconds);
        foreach (var callback in _frameCallbacks.ToArray())
        {
            callback.Invoke([timeValue], JsValue.Undefined);
        }

        _document.Flush();
    }

    public void DispatchKey(string key)
    {
        if (!_keyCallbacks.TryGetValue(key, out var callbacks))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"No JsEngine key handler registered for {key}."));
            return;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Dispatching {key} to JsEngine."));
        foreach (var callback in callbacks.ToArray())
        {
            callback.Invoke([], JsValue.Undefined);
        }

        _document.Flush();
    }

    public void DispatchClick(double x, double y)
    {
        if (_clickCallbacks.Count == 0)
        {
            return;
        }

        var xValue = new JsValue(x);
        var yValue = new JsValue(y);
        foreach (var callback in _clickCallbacks.ToArray())
        {
            callback.Invoke([xValue, yValue], JsValue.Undefined);
        }

        _document.Flush();
    }

    public void Dispose()
    {
        _demoProcessHost.Dispose();
        _engine.Dispose();
    }

    private JsObject CreateConsoleObject()
    {
        var console = new JsObject();
        SetProperty(console, "log", CreateHostFunction(args =>
        {
            Console.WriteLine(FormatConsoleArguments(args));
            return JsValue.Undefined;
        }));
        return console;
    }

    private JsObject CreateSvgObject()
    {
        var svg = new JsObject();
        SetProperty(svg, "id", CreateHostFunction(args =>
        {
            var id = GetString(args, 0);
            return CreateElementObject(id);
        }));
        SetProperty(svg, "layer", JsValue.FromObjectUnsafe(CreateLayerObject()));
        return svg;
    }

    private JsObject CreateSlideObject()
    {
        var slide = new JsObject();
        SetProperty(slide, "onFrame", CreateHostFunction(args =>
        {
            if (TryGetCallable(args, 0, out var callback))
            {
                _frameCallbacks.Add(callback);
            }

            return JsValue.Undefined;
        }));
        SetProperty(slide, "onKey", CreateHostFunction(args =>
        {
            var key = GetString(args, 0);
            if (!TryGetCallable(args, 1, out var callback))
            {
                return JsValue.Undefined;
            }

            if (!_keyCallbacks.TryGetValue(key, out var callbacks))
            {
                callbacks = [];
                _keyCallbacks[key] = callbacks;
            }

            callbacks.Add(callback);
            return JsValue.Undefined;
        }));
        SetProperty(slide, "onClick", CreateHostFunction(args =>
        {
            if (TryGetCallable(args, 0, out var callback))
            {
                _clickCallbacks.Add(callback);
            }

            return JsValue.Undefined;
        }));
        return slide;
    }

    private JsObject CreateDemoObject()
    {
        var demo = new JsObject();
        SetProperty(demo, "startExpress", CreateHostFunction(_ =>
            new JsValue(_demoProcessHost.StartExpress())));
        SetProperty(demo, "stopExpress", CreateHostFunction(_ =>
        {
            _demoProcessHost.StopExpress();
            return JsValue.Undefined;
        }));
        SetProperty(demo, "curl", CreateHostFunction(args =>
            new JsValue(_demoProcessHost.RunCurl(GetString(args, 0)))));
        SetProperty(demo, "hostOutput", CreateHostFunction(_ =>
            new JsValue(_demoProcessHost.HostOutput())));
        SetProperty(demo, "curlOutput", CreateHostFunction(_ =>
            new JsValue(_demoProcessHost.CurlOutput())));
        SetProperty(demo, "expressStatus", CreateHostFunction(_ =>
            new JsValue(_demoProcessHost.ExpressStatus())));
        SetProperty(demo, "isExpressReady", CreateHostFunction(_ =>
            new JsValue(_demoProcessHost.IsExpressReady())));
        SetProperty(demo, "isExpressRunning", CreateHostFunction(_ =>
            new JsValue(_demoProcessHost.IsExpressRunning())));
        return demo;
    }

    private JsObject CreatePresentationObject()
    {
        var presentation = new JsObject();
        SetProperty(presentation, "count", CreateHostFunction(_ =>
            new JsValue(_deck?.Count ?? 0)));
        SetProperty(presentation, "current", CreateHostFunction(_ =>
            new JsValue(_deck?.CurrentIndex ?? 0)));
        SetProperty(presentation, "path", CreateHostFunction(args =>
        {
            if (_deck is null)
            {
                return new JsValue(string.Empty);
            }

            return new JsValue(_deck.GetPath(GetInt32(args, 0)));
        }));
        SetProperty(presentation, "load", CreateHostFunction(args =>
        {
            if (_deck is null)
            {
                return JsValue.Undefined;
            }

            var displayCount = args.Count > 2
                ? GetInt32(args, 2)
                : _deck.DisplayCount;
            var path = args.Count > 0 && args[0].Kind == JsValueKind.String
                ? _deck.Load(GetString(args, 0), GetInt32(args, 1), displayCount)
                : _deck.Load(GetInt32(args, 0));
            _document.Load(path);
            _presentationLoaded?.Invoke(_deck, path);
            Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Presentation page {_deck.CurrentPageNumber}/{_deck.DisplayCount}: {Path.GetFileName(path)}"));
            return new JsValue(_deck.CurrentIndex);
        }));
        return presentation;
    }

    private JsObject CreateElementObject(string id)
    {
        var element = new JsObject();
        SetProperty(element, "set", CreateHostFunction(args =>
        {
            _document.SetAttribute(id, GetString(args, 0), GetString(args, 1));
            return JsValue.Undefined;
        }));
        SetProperty(element, "text", CreateHostFunction(args =>
        {
            _document.SetText(id, GetString(args, 0));
            return JsValue.Undefined;
        }));
        SetProperty(element, "transform", CreateHostFunction(args =>
        {
            _document.SetAttribute(id, "transform", GetString(args, 0));
            return JsValue.Undefined;
        }));
        return element;
    }

    private JsObject CreateLayerObject()
    {
        var layer = new JsObject();
        SetProperty(layer, "clear", CreateHostFunction(_ =>
        {
            _document.ClearLayer();
            return JsValue.Undefined;
        }));
        SetProperty(layer, "rect", CreateHostFunction(args =>
        {
            _document.AddLayerElement(
                "rect",
                GetString(args, 0),
                [
                    new("x", GetDouble(args, 1).ToString(CultureInfo.InvariantCulture)),
                    new("y", GetDouble(args, 2).ToString(CultureInfo.InvariantCulture)),
                    new("width", GetDouble(args, 3).ToString(CultureInfo.InvariantCulture)),
                    new("height", GetDouble(args, 4).ToString(CultureInfo.InvariantCulture)),
                    new("fill", GetString(args, 5)),
                    new("opacity", GetDouble(args, 6, 1).ToString(CultureInfo.InvariantCulture))
                ]);
            return CreateElementObject(GetString(args, 0));
        }));
        SetProperty(layer, "circle", CreateHostFunction(args =>
        {
            _document.AddLayerElement(
                "circle",
                GetString(args, 0),
                [
                    new("cx", GetDouble(args, 1).ToString(CultureInfo.InvariantCulture)),
                    new("cy", GetDouble(args, 2).ToString(CultureInfo.InvariantCulture)),
                    new("r", GetDouble(args, 3).ToString(CultureInfo.InvariantCulture)),
                    new("fill", GetString(args, 4)),
                    new("opacity", GetDouble(args, 5, 1).ToString(CultureInfo.InvariantCulture))
                ]);
            return CreateElementObject(GetString(args, 0));
        }));
        SetProperty(layer, "path", CreateHostFunction(args =>
        {
            _document.AddLayerElement(
                "path",
                GetString(args, 0),
                [
                    new("d", GetString(args, 1)),
                    new("fill", GetString(args, 2)),
                    new("stroke", GetString(args, 3)),
                    new("stroke-width", GetDouble(args, 4, 1).ToString(CultureInfo.InvariantCulture)),
                    new("opacity", GetDouble(args, 5, 1).ToString(CultureInfo.InvariantCulture))
                ]);
            return CreateElementObject(GetString(args, 0));
        }));
        SetProperty(layer, "polygon", CreateHostFunction(args =>
        {
            _document.AddLayerElement(
                "polygon",
                GetString(args, 0),
                [
                    new("points", GetString(args, 1)),
                    new("fill", GetString(args, 2)),
                    new("stroke", GetString(args, 3)),
                    new("stroke-width", GetDouble(args, 4, 1).ToString(CultureInfo.InvariantCulture)),
                    new("opacity", GetDouble(args, 5, 1).ToString(CultureInfo.InvariantCulture))
                ]);
            return CreateElementObject(GetString(args, 0));
        }));
        SetProperty(layer, "text", CreateHostFunction(args =>
        {
            _document.AddLayerText(
                GetString(args, 0),
                GetString(args, 1),
                GetDouble(args, 2),
                GetDouble(args, 3),
                GetDouble(args, 4, 28),
                GetString(args, 5),
                GetDouble(args, 6, 1));
            return CreateElementObject(GetString(args, 0));
        }));
        return layer;
    }

    private HostFunction CreateHostFunction(JsSimpleHandler handler)
    {
        return new HostFunction(handler, realmState: null, isConstructor: false)
        {
            Realm = _engine.GlobalObject
        };
    }

    private static bool TryGetCallable(IReadOnlyList<JsValue> args, int index, out IJsCallable callback)
    {
        if (args.Count > index && args[index].ObjectValue is IJsCallable callable)
        {
            callback = callable;
            return true;
        }

        callback = null!;
        return false;
    }

    private static string GetString(IReadOnlyList<JsValue> args, int index)
    {
        if (args.Count <= index)
        {
            return string.Empty;
        }

        var value = args[index];
        return value.Kind == JsValueKind.String
            ? value.ObjectValue?.ToString() ?? string.Empty
            : value.ToString();
    }

    private static int GetInt32(IReadOnlyList<JsValue> args, int index)
    {
        if (args.Count <= index)
        {
            return 0;
        }

        var text = args[index].ToString();
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static double GetDouble(IReadOnlyList<JsValue> args, int index, double defaultValue = 0)
    {
        if (args.Count <= index)
        {
            return defaultValue;
        }

        var text = args[index].ToString();
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static string FormatConsoleArguments(IReadOnlyList<JsValue> args)
    {
        return string.Join(" ", args.Select(static arg => arg.ToString()));
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
}

internal sealed class ExpressDemoProcessHost : IDisposable
{
    private const int OfficialExpressPort = 3000;
    private readonly object _sync = new();
    private readonly string _nodeHostDirectory;
    private readonly StringBuilder _hostOutput = new();
    private readonly StringBuilder _curlOutput = new();
    private Process? _serverProcess;
    private bool _serverReady;
    private int _curlSequence;

    public ExpressDemoProcessHost(string baseDirectory)
    {
        _nodeHostDirectory = FindNodeHostDirectory(baseDirectory);
    }

    public string StartExpress()
    {
        lock (_sync)
        {
            if (IsExpressRunningCore())
            {
                AppendHost("Express host is already running.");
                return "already-running";
            }

            _serverReady = false;
            _hostOutput.Clear();
            if (string.IsNullOrEmpty(_nodeHostDirectory))
            {
                AppendHost("Could not find examples/NodeHostDemo.");
                return "missing-node-host-demo";
            }

            var startInfo = CreateExpressStartInfo();
            AppendHost("$ " + startInfo.FileName + " " + startInfo.Arguments);
            startInfo.WorkingDirectory = _nodeHostDirectory;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            process.OutputDataReceived += (_, eventArgs) => AppendHost(eventArgs.Data);
            process.ErrorDataReceived += (_, eventArgs) => AppendHost(eventArgs.Data);
            process.Exited += (_, _) =>
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_serverProcess, process))
                    {
                        _serverReady = false;
                    }
                }

                AppendHost(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[process exited {process.ExitCode}]"));
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _serverProcess = process;
                _ = MonitorReadinessAsync(process);
                return "started";
            }
            catch (Exception ex)
            {
                process.Dispose();
                AppendHost(string.Create(CultureInfo.InvariantCulture, $"Failed to start: {ex.Message}"));
                return "failed";
            }
        }
    }

    public string RunCurl(string path)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(path)
            ? "/"
            : path.Trim();
        if (!normalizedPath.StartsWith('/'))
        {
            normalizedPath = "/" + normalizedPath;
        }

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"http://localhost:{OfficialExpressPort}{normalizedPath}");
        var sequence = Interlocked.Increment(ref _curlSequence);
        lock (_sync)
        {
            _curlOutput.Clear();
            AppendCurl(string.Create(CultureInfo.InvariantCulture, $"$ curl -i -sS {url}"));
            if (!IsExpressRunningCore())
            {
                AppendCurl("Express host is not running yet.");
                return "host-not-running";
            }

            if (!_serverReady)
            {
                AppendCurl("Waiting for upstream Express listener on localhost:3000...");
            }
        }

        _ = RunCurlAsync(url, sequence);
        return "started";
    }

    public void StopExpress()
    {
        Process? process;
        lock (_sync)
        {
            process = _serverProcess;
            _serverProcess = null;
            _serverReady = false;
        }

        if (process is null)
        {
            AppendHost("Express host is not running.");
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            AppendHost(string.Create(CultureInfo.InvariantCulture, $"Failed to stop: {ex.Message}"));
        }
        finally
        {
            process.Dispose();
            AppendHost("Express host stopped.");
        }
    }

    public string HostOutput()
    {
        lock (_sync)
        {
            return _hostOutput.ToString();
        }
    }

    public string CurlOutput()
    {
        lock (_sync)
        {
            return _curlOutput.ToString();
        }
    }

    public bool IsExpressRunning()
    {
        lock (_sync)
        {
            return IsExpressRunningCore();
        }
    }

    public bool IsExpressReady()
    {
        lock (_sync)
        {
            return _serverReady && IsExpressRunningCore();
        }
    }

    public string ExpressStatus()
    {
        lock (_sync)
        {
            if (!IsExpressRunningCore())
            {
                return "stopped";
            }

            return _serverReady
                ? "ready"
                : "starting";
        }
    }

    public void Dispose()
    {
        StopExpress();
    }

    private async Task RunCurlAsync(string url, int sequence)
    {
        if (!await WaitForExpressReadyAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false))
        {
            if (sequence == Volatile.Read(ref _curlSequence))
            {
                AppendCurl("Upstream Express listener did not become ready within 30 seconds.");
            }

            return;
        }

        var startInfo = new ProcessStartInfo("curl", $"-i -sS {url}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                AppendCurl("Failed to start curl.");
                return;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            if (sequence != Volatile.Read(ref _curlSequence))
            {
                return;
            }

            AppendCurl(output);
            AppendCurl(error);
            AppendCurl(string.Create(CultureInfo.InvariantCulture, $"[curl exited {process.ExitCode}]"));
        }
        catch (Exception ex)
        {
            AppendCurl(string.Create(CultureInfo.InvariantCulture, $"curl failed: {ex.Message}"));
        }
    }

    private async Task MonitorReadinessAsync(Process process)
    {
        AppendHost("[waiting for upstream Express listener on localhost:3000]");

        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!IsCurrentProcessRunning(process))
            {
                return;
            }

            if (await CanConnectToExpressAsync().ConfigureAwait(false))
            {
                lock (_sync)
                {
                    if (!ReferenceEquals(_serverProcess, process) || process.HasExited)
                    {
                        return;
                    }

                    _serverReady = true;
                }

                AppendHost("[listener ready on http://localhost:3000]");
                return;
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        if (IsCurrentProcessRunning(process))
        {
            AppendHost("[upstream listener did not become ready within 30 seconds]");
        }
    }

    private async Task<bool> WaitForExpressReadyAsync(TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (_sync)
            {
                if (!IsExpressRunningCore())
                {
                    return false;
                }

                if (_serverReady)
                {
                    return true;
                }
            }

            await Task.Delay(250).ConfigureAwait(false);
        }

        return false;
    }

    private bool IsCurrentProcessRunning(Process process)
    {
        lock (_sync)
        {
            return ReferenceEquals(_serverProcess, process) && !process.HasExited;
        }
    }

    private static async Task<bool> CanConnectToExpressAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", OfficialExpressPort)
                .WaitAsync(TimeSpan.FromMilliseconds(250))
                .ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    private bool IsExpressRunningCore()
    {
        return _serverProcess is { HasExited: false };
    }

    private ProcessStartInfo CreateExpressStartInfo()
    {
        var upstreamPackage = Path.Combine(
            _nodeHostDirectory,
            "third_party",
            "express",
            "package.json");
        if (File.Exists(upstreamPackage))
        {
            return new ProcessStartInfo("npm", "run official-express-mvc");
        }

        if (OperatingSystem.IsWindows())
        {
            return new ProcessStartInfo("cmd", "/c npm run prepare:official-express && npm run official-express-mvc");
        }

        return new ProcessStartInfo("/bin/sh", "-lc \"npm run prepare:official-express && npm run official-express-mvc\"");
    }

    private void AppendHost(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        lock (_sync)
        {
            _hostOutput.AppendLine(line);
            TrimOutput(_hostOutput);
        }
    }

    private void AppendCurl(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_sync)
        {
            _curlOutput.AppendLine(text.TrimEnd());
            TrimOutput(_curlOutput);
        }
    }

    private static void TrimOutput(StringBuilder builder)
    {
        const int MaxCharacters = 6_000;
        if (builder.Length <= MaxCharacters)
        {
            return;
        }

        builder.Remove(0, builder.Length - MaxCharacters);
    }

    private static string FindNodeHostDirectory(string baseDirectory)
    {
        foreach (var candidateRoot in EnumerateCandidateRoots(baseDirectory))
        {
            var candidate = Path.Combine(candidateRoot, "examples", "NodeHostDemo");
            if (File.Exists(Path.Combine(candidate, "NodeHostDemo.csproj")))
            {
                return candidate;
            }
        }

        return string.Empty;
    }

    private static IEnumerable<string> EnumerateCandidateRoots(string baseDirectory)
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }

        current = new DirectoryInfo(baseDirectory);
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}

internal sealed class SvgSlideDocument
{
    private const string LayerId = "jsengine-layer";
    private readonly Action _render;
    private readonly Dictionary<string, XElement> _elementsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Bitmap> _imageCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Bitmap> _maskedImageCache = new(StringComparer.Ordinal);
    private XDocument _document;
    private bool _dirty;
    private double _viewBoxWidth = 1280;
    private double _viewBoxHeight = 720;
    public SvgSlideDocument(string sourcePath, string renderedPath, Action render)
    {
        _document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
        RenderedPath = renderedPath;
        _render = render;
        _dirty = true;
        RebuildElementIndex();
    }

    public string RenderedPath { get; }

    public void Load(string sourcePath)
    {
        _document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
        ReadViewBox();
        RebuildElementIndex();
        _dirty = true;
    }

    public int Preload(IReadOnlyList<string> sourcePaths)
    {
        var originalDocument = _document;
        var originalElementsById = _elementsById.ToArray();
        var originalViewBoxWidth = _viewBoxWidth;
        var originalViewBoxHeight = _viewBoxHeight;
        var originalDirty = _dirty;
        var preloaded = 0;

        try
        {
            foreach (var sourcePath in sourcePaths)
            {
                _document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
                RebuildElementIndex();
                preloaded += PreloadCurrentDocumentImages();
            }
        }
        finally
        {
            _document = originalDocument;
            _elementsById.Clear();
            foreach (var (key, value) in originalElementsById)
            {
                _elementsById[key] = value;
            }

            _viewBoxWidth = originalViewBoxWidth;
            _viewBoxHeight = originalViewBoxHeight;
            _dirty = originalDirty;
        }

        return preloaded;
    }

    public bool ContainsElement(string id)
    {
        return _elementsById.ContainsKey(id);
    }

    public string GetElementText(string id)
    {
        return GetElementById(id).Value;
    }

    public int CountElementsWithAttributePrefix(string idPrefix, string name, string value)
    {
        var count = 0;
        foreach (var (id, element) in _elementsById)
        {
            if (id.StartsWith(idPrefix, StringComparison.Ordinal) &&
                string.Equals((string?)element.Attribute(name), value, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    public void SetAttribute(string id, string name, string value)
    {
        var element = GetElementById(id);
        element.SetAttributeValue(name, value);
        _dirty = true;
    }

    public void SetText(string id, string value)
    {
        var element = GetElementById(id);
        element.Value = value;
        _dirty = true;
    }

    public void ClearLayer()
    {
        if (_document.Root is null)
        {
            return;
        }

        GetLayer(createIfMissing: false)?.RemoveNodes();
        RebuildElementIndex();
        _dirty = true;
    }

    public void AddLayerElement(string localName, string id, IReadOnlyList<XAttribute> attributes)
    {
        if (_document.Root is null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        RemoveElementIfExists(id);
        var element = new XElement(_document.Root.Name.Namespace + localName, new XAttribute("id", id));
        foreach (var attribute in attributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.Value))
            {
                element.SetAttributeValue(attribute.Name, attribute.Value);
            }
        }

        GetLayer(createIfMissing: true)?.Add(element);
        RebuildElementIndex();
        _dirty = true;
    }

    public void AddLayerText(
        string id,
        string text,
        double x,
        double y,
        double fontSize,
        string fill,
        double opacity)
    {
        if (_document.Root is null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        RemoveElementIfExists(id);
        var element = new XElement(
            _document.Root.Name.Namespace + "text",
            new XAttribute("id", id),
            new XAttribute("x", x.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("y", y.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("font-size", fontSize.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("fill", string.IsNullOrWhiteSpace(fill) ? "#ffffff" : fill),
            new XAttribute("opacity", opacity.ToString(CultureInfo.InvariantCulture)),
            text);
        GetLayer(createIfMissing: true)?.Add(element);
        RebuildElementIndex();
        _dirty = true;
    }

    public void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        _document.Save(RenderedPath, SaveOptions.DisableFormatting);
        ReadViewBox();
        RebuildElementIndex();
        _dirty = false;
        _render();
    }

    public void Render(DrawingContext context, Rect bounds)
    {
        if (_document.Root is null)
        {
            return;
        }

        var scale = Math.Min(bounds.Width / _viewBoxWidth, bounds.Height / _viewBoxHeight);
        var offsetX = bounds.X + (bounds.Width - _viewBoxWidth * scale) / 2;
        var offsetY = bounds.Y + (bounds.Height - _viewBoxHeight * scale) / 2;

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offsetX, offsetY)))
        {
            var state = SvgRenderState.Default;
            foreach (var child in _document.Root.Elements())
            {
                RenderElement(context, child, state);
            }
        }
    }

    public bool TryMapToSvgPoint(Point point, Rect bounds, out double x, out double y)
    {
        var scale = Math.Min(bounds.Width / _viewBoxWidth, bounds.Height / _viewBoxHeight);
        var offsetX = bounds.X + (bounds.Width - _viewBoxWidth * scale) / 2;
        var offsetY = bounds.Y + (bounds.Height - _viewBoxHeight * scale) / 2;
        x = (point.X - offsetX) / scale;
        y = (point.Y - offsetY) / scale;
        return x >= 0 && x <= _viewBoxWidth && y >= 0 && y <= _viewBoxHeight;
    }

    public SvgMaskAnalysis AnalyzeGeneratedImageMasks()
    {
        var groupCount = 0;
        var hasTransparentPixels = false;
        var hasOpaquePixels = false;

        foreach (var group in _document.Descendants().Where(static element => element.Name.LocalName == "g"))
        {
            if (!TryResolveMaskedImageGroup(group, out _, out _, out var maskImage))
            {
                continue;
            }

            groupCount++;
            if (hasTransparentPixels && hasOpaquePixels)
            {
                continue;
            }

            var maskHref = ReadHref(maskImage);
            if (string.IsNullOrWhiteSpace(maskHref) || !TryReadDataUrlBytes(maskHref, out var maskBytes))
            {
                continue;
            }

            using var maskBitmap = SKBitmap.Decode(maskBytes);
            if (maskBitmap is null)
            {
                continue;
            }

            for (var y = 0; y < maskBitmap.Height; y++)
            {
                for (var x = 0; x < maskBitmap.Width; x++)
                {
                    var mask = maskBitmap.GetPixel(x, y);
                    var alpha = (int)Math.Round(mask.Red * 0.2126 + mask.Green * 0.7152 + mask.Blue * 0.0722);
                    hasTransparentPixels |= alpha <= 8;
                    hasOpaquePixels |= alpha >= 247;
                    if (hasTransparentPixels && hasOpaquePixels)
                    {
                        break;
                    }
                }

                if (hasTransparentPixels && hasOpaquePixels)
                {
                    break;
                }
            }
        }

        return new SvgMaskAnalysis(groupCount, hasTransparentPixels, hasOpaquePixels);
    }

    private int PreloadCurrentDocumentImages()
    {
        var preloaded = 0;
        foreach (var imageElement in _document.Descendants().Where(static element => element.Name.LocalName == "image"))
        {
            var href = ReadHref(imageElement);
            if (!string.IsNullOrWhiteSpace(href) && GetBitmap(href) is not null)
            {
                preloaded++;
            }
        }

        foreach (var group in _document.Descendants().Where(static element => element.Name.LocalName == "g"))
        {
            if (!TryResolveMaskedImageGroup(group, out _, out var colorImage, out var maskImage))
            {
                continue;
            }

            var colorHref = ReadHref(colorImage);
            var maskHref = ReadHref(maskImage);
            if (!string.IsNullOrWhiteSpace(colorHref) &&
                !string.IsNullOrWhiteSpace(maskHref) &&
                GetMaskedBitmap(colorHref, maskHref) is not null)
            {
                preloaded++;
            }
        }

        return preloaded;
    }

    private XElement GetElementById(string id)
    {
        if (!_elementsById.TryGetValue(id, out var element))
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"SVG element not found: {id}"));
        }

        return element;
    }

    private XElement? GetLayer(bool createIfMissing)
    {
        if (_document.Root is null)
        {
            return null;
        }

        var layer = _document
            .Descendants()
            .FirstOrDefault(candidate =>
                string.Equals((string?)candidate.Attribute("id"), LayerId, StringComparison.Ordinal));
        if (layer is not null || !createIfMissing)
        {
            return layer;
        }

        layer = new XElement(_document.Root.Name.Namespace + "g", new XAttribute("id", LayerId));
        _document.Root.Add(layer);
        return layer;
    }

    private void RemoveElementIfExists(string id)
    {
        if (_elementsById.TryGetValue(id, out var existing))
        {
            existing.Remove();
            _elementsById.Remove(id);
        }
    }

    private void RebuildElementIndex()
    {
        _elementsById.Clear();
        foreach (var element in _document.Descendants())
        {
            var id = (string?)element.Attribute("id");
            if (!string.IsNullOrEmpty(id))
            {
                _elementsById[id] = element;
            }
        }
    }

    private void ReadViewBox()
    {
        if (_document.Root is null)
        {
            return;
        }

        var viewBox = (string?)_document.Root.Attribute("viewBox");
        if (string.IsNullOrWhiteSpace(viewBox))
        {
            _viewBoxWidth = ReadDouble(_document.Root, "width", _viewBoxWidth, SvgRenderState.Default);
            _viewBoxHeight = ReadDouble(_document.Root, "height", _viewBoxHeight, SvgRenderState.Default);
            return;
        }

        var parts = viewBox.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 4 &&
            double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var width) &&
            double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var height))
        {
            _viewBoxWidth = width;
            _viewBoxHeight = height;
        }
    }

    private void RenderElement(DrawingContext context, XElement element, SvgRenderState inheritedState)
    {
        var state = inheritedState.Inherit(element);
        if (state.Opacity <= 0)
        {
            return;
        }

        var localName = element.Name.LocalName;
        if (localName == "defs" || localName == "clipPath")
        {
            return;
        }

        using var transform = PushElementTransform(context, element);
        using var clip = PushElementClip(context, element);
        if (localName == "g" && TryRenderMaskedImageGroup(context, element, state))
        {
            return;
        }

        if (localName is "g" or "svg")
        {
            foreach (var child in element.Elements())
            {
                RenderElement(context, child, state);
            }

            return;
        }

        switch (localName)
        {
            case "rect":
                RenderRect(context, element, state);
                break;
            case "circle":
                RenderCircle(context, element, state);
                break;
            case "line":
                RenderLine(context, element, state);
                break;
            case "path":
                RenderPath(context, element, state);
                break;
            case "polygon":
                RenderPolygon(context, element, state);
                break;
            case "text":
                RenderText(context, element, state);
                break;
            case "use":
                RenderUse(context, element, state);
                break;
            case "image":
                RenderImage(context, element, state);
                break;
        }
    }

    private static IDisposable? PushElementTransform(DrawingContext context, XElement element)
    {
        var transform = (string?)element.Attribute("transform");
        if (string.IsNullOrWhiteSpace(transform))
        {
            return null;
        }

        if (TryParseTransform(transform, out var matrix))
        {
            return context.PushTransform(matrix);
        }

        return null;
    }

    private IDisposable? PushElementClip(DrawingContext context, XElement element)
    {
        var clipPath = (string?)element.Attribute("clip-path");
        if (string.IsNullOrWhiteSpace(clipPath) ||
            !TryParseUrlReference(clipPath, out var clipId) ||
            !_elementsById.TryGetValue(clipId, out var clipElement))
        {
            return null;
        }

        var geometries = new GeometryGroup();
        foreach (var child in clipElement.Elements())
        {
            if (child.Name.LocalName != "path")
            {
                continue;
            }

            var data = ReadAttribute(child, "d", SvgRenderState.Default);
            if (!string.IsNullOrWhiteSpace(data))
            {
                geometries.Children.Add(Geometry.Parse(data));
            }
        }

        return geometries.Children.Count == 0
            ? null
            : context.PushGeometryClip(geometries);
    }

    private static void RenderRect(DrawingContext context, XElement element, SvgRenderState state)
    {
        var x = ReadDouble(element, "x", 0, state);
        var y = ReadDouble(element, "y", 0, state);
        var width = ReadDouble(element, "width", 0, state);
        var height = ReadDouble(element, "height", 0, state);
        var rx = ReadDouble(element, "rx", 0, state);
        var fill = state.CreateFillBrush();
        var pen = state.CreatePen();
        context.DrawRectangle(fill, pen, new Rect(x, y, width, height), rx, rx);
    }

    private static void RenderCircle(DrawingContext context, XElement element, SvgRenderState state)
    {
        var cx = ReadDouble(element, "cx", 0, state);
        var cy = ReadDouble(element, "cy", 0, state);
        var r = ReadDouble(element, "r", 0, state);
        var fill = state.CreateFillBrush();
        var pen = state.CreatePen();
        context.DrawEllipse(fill, pen, new Point(cx, cy), r, r);
    }

    private static void RenderLine(DrawingContext context, XElement element, SvgRenderState state)
    {
        var x1 = ReadDouble(element, "x1", 0, state);
        var y1 = ReadDouble(element, "y1", 0, state);
        var x2 = ReadDouble(element, "x2", 0, state);
        var y2 = ReadDouble(element, "y2", 0, state);
        var pen = state.CreatePen();
        if (pen is null)
        {
            return;
        }

        context.DrawLine(pen, new Point(x1, y1), new Point(x2, y2));
    }

    private static void RenderPath(DrawingContext context, XElement element, SvgRenderState state)
    {
        var d = ReadAttribute(element, "d", state);
        if (string.IsNullOrWhiteSpace(d))
        {
            return;
        }

        var geometry = Geometry.Parse(d);
        var fill = state.CreateFillBrush();
        var pen = state.CreatePen();
        context.DrawGeometry(fill, pen, geometry);
    }

    private static void RenderPolygon(DrawingContext context, XElement element, SvgRenderState state)
    {
        var points = ReadAttribute(element, "points", state);
        if (string.IsNullOrWhiteSpace(points))
        {
            return;
        }

        var coordinates = points.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (coordinates.Length < 6 || coordinates.Length % 2 != 0)
        {
            return;
        }

        var data = new StringBuilder();
        for (var index = 0; index < coordinates.Length; index += 2)
        {
            if (!double.TryParse(coordinates[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(coordinates[index + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                return;
            }

            data.Append(index == 0 ? "M " : " L ");
            data.Append(x.ToString(CultureInfo.InvariantCulture));
            data.Append(' ');
            data.Append(y.ToString(CultureInfo.InvariantCulture));
        }

        data.Append(" Z");
        var geometry = Geometry.Parse(data.ToString());
        var fill = state.CreateFillBrush();
        var pen = state.CreatePen();
        context.DrawGeometry(fill, pen, geometry);
    }

    private static void RenderText(DrawingContext context, XElement element, SvgRenderState state)
    {
        var x = ReadDouble(element, "x", 0, state);
        var y = ReadDouble(element, "y", 0, state);
        var fontSize = ReadDouble(element, "font-size", 20, state);
        var brush = state.CreateFillBrush() ?? Brushes.White;
        var formatted = new FormattedText(
            element.Value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            fontSize,
            brush);

        var anchor = ReadAttribute(element, "text-anchor", state);
        if (string.Equals(anchor, "middle", StringComparison.OrdinalIgnoreCase))
        {
            x -= formatted.Width / 2;
        }
        else if (string.Equals(anchor, "end", StringComparison.OrdinalIgnoreCase))
        {
            x -= formatted.Width;
        }

        context.DrawText(formatted, new Point(x, y - fontSize));
    }

    private void RenderUse(DrawingContext context, XElement element, SvgRenderState state)
    {
        var reference = ReadHref(element);
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith('#'))
        {
            return;
        }

        var id = reference[1..];
        if (!_elementsById.TryGetValue(id, out var target))
        {
            return;
        }

        var x = ReadDouble(element, "x", 0, state);
        var y = ReadDouble(element, "y", 0, state);
        using var translation = context.PushTransform(Matrix.CreateTranslation(x, y));
        if (target.Name.LocalName == "g")
        {
            foreach (var child in target.Elements())
            {
                RenderElement(context, child, state);
            }

            return;
        }

        RenderElement(context, target, state);
    }

    private void RenderImage(DrawingContext context, XElement element, SvgRenderState state)
    {
        var href = ReadHref(element);
        if (string.IsNullOrWhiteSpace(href))
        {
            return;
        }

        var image = GetBitmap(href);
        if (image is null)
        {
            return;
        }

        var x = ReadDouble(element, "x", 0, state);
        var y = ReadDouble(element, "y", 0, state);
        var width = ReadDouble(element, "width", image.Size.Width, state);
        var height = ReadDouble(element, "height", image.Size.Height, state);
        context.DrawImage(image, new Rect(x, y, width, height));
    }

    private bool TryRenderMaskedImageGroup(DrawingContext context, XElement group, SvgRenderState state)
    {
        if (!TryResolveMaskedImageGroup(group, out var colorUse, out var colorImage, out var maskImage))
        {
            return false;
        }

        var colorHref = ReadHref(colorImage);
        var maskHref = ReadHref(maskImage);
        if (string.IsNullOrWhiteSpace(colorHref) || string.IsNullOrWhiteSpace(maskHref))
        {
            return false;
        }

        var image = GetMaskedBitmap(colorHref, maskHref);
        if (image is null)
        {
            return false;
        }

        using var opacity = state.Opacity < 1
            ? (IDisposable)context.PushOpacity(state.Opacity)
            : null;
        using var useTransform = PushElementTransform(context, colorUse);

        var x = ReadDouble(colorImage, "x", 0, state);
        var y = ReadDouble(colorImage, "y", 0, state);
        var width = ReadDouble(colorImage, "width", image.Size.Width, state);
        var height = ReadDouble(colorImage, "height", image.Size.Height, state);
        context.DrawImage(image, new Rect(x, y, width, height));
        return true;
    }

    private bool TryResolveMaskedImageGroup(
        XElement group,
        out XElement colorUse,
        out XElement colorImage,
        out XElement maskImage)
    {
        colorUse = null!;
        colorImage = null!;
        maskImage = null!;

        var maskReference = (string?)group.Attribute("mask");
        if (string.IsNullOrWhiteSpace(maskReference) ||
            !TryParseUrlReference(maskReference, out var maskId) ||
            !_elementsById.TryGetValue(maskId, out var maskElement))
        {
            return false;
        }

        colorUse = group
            .Elements()
            .FirstOrDefault(static child => child.Name.LocalName == "use")!;
        if (colorUse is null ||
            !TryGetReferencedElement(colorUse, out colorImage) ||
            colorImage.Name.LocalName != "image" ||
            !TryGetMaskImage(maskElement, out maskImage))
        {
            return false;
        }

        return true;
    }

    private bool TryGetReferencedElement(XElement useElement, out XElement target)
    {
        var reference = ReadHref(useElement);
        if (!string.IsNullOrWhiteSpace(reference) &&
            reference.StartsWith('#') &&
            _elementsById.TryGetValue(reference[1..], out target!))
        {
            return true;
        }

        target = null!;
        return false;
    }

    private bool TryGetMaskImage(XElement maskElement, out XElement imageElement)
    {
        foreach (var useElement in maskElement.Descendants().Where(static element => element.Name.LocalName == "use"))
        {
            if (TryGetReferencedElement(useElement, out var referencedElement) &&
                referencedElement.Name.LocalName == "image")
            {
                imageElement = referencedElement;
                return true;
            }
        }

        imageElement = null!;
        return false;
    }

    private Bitmap? GetBitmap(string href)
    {
        if (_imageCache.TryGetValue(href, out var cached))
        {
            return cached;
        }

        if (!href.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var commaIndex = href.IndexOf(',');
        if (commaIndex < 0)
        {
            return null;
        }

        var header = href[..commaIndex];
        var payload = href[(commaIndex + 1)..];
        if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bytes = Convert.FromBase64String(payload);
        using var stream = new MemoryStream(bytes);
        var bitmap = new Bitmap(stream);
        _imageCache[href] = bitmap;
        return bitmap;
    }

    private Bitmap? GetMaskedBitmap(string colorHref, string maskHref)
    {
        var cacheKey = string.Concat(colorHref, "|", maskHref);
        if (_maskedImageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        if (!TryReadDataUrlBytes(colorHref, out var colorBytes) ||
            !TryReadDataUrlBytes(maskHref, out var maskBytes))
        {
            return null;
        }

        using var colorBitmap = SKBitmap.Decode(colorBytes);
        using var maskBitmap = SKBitmap.Decode(maskBytes);
        if (colorBitmap is null || maskBitmap is null)
        {
            return null;
        }

        var width = colorBitmap.Width;
        var height = colorBitmap.Height;
        var outputInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var output = new SKBitmap(outputInfo);

        for (var y = 0; y < height; y++)
        {
            var maskY = y * maskBitmap.Height / height;
            for (var x = 0; x < width; x++)
            {
                var maskX = x * maskBitmap.Width / width;
                var color = colorBitmap.GetPixel(x, y);
                var mask = maskBitmap.GetPixel(maskX, maskY);
                var alpha = (byte)Math.Clamp(
                    (int)Math.Round(mask.Red * 0.2126 + mask.Green * 0.7152 + mask.Blue * 0.0722),
                    0,
                    255);
                output.SetPixel(x, y, new SKColor(color.Red, color.Green, color.Blue, alpha));
            }
        }

        using var image = SKImage.FromBitmap(output);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(encoded.ToArray());
        var bitmap = new Bitmap(stream);
        _maskedImageCache[cacheKey] = bitmap;
        return bitmap;
    }

    private static bool TryReadDataUrlBytes(string href, out byte[] bytes)
    {
        bytes = [];
        if (!href.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var commaIndex = href.IndexOf(',');
        if (commaIndex < 0)
        {
            return false;
        }

        var header = href[..commaIndex];
        if (!header.Contains(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bytes = Convert.FromBase64String(href[(commaIndex + 1)..]);
        return true;
    }

    private static double ReadDouble(XElement element, string name, double defaultValue, SvgRenderState state)
    {
        var value = ReadAttribute(element, name, state);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        value = value.Trim();
        if (value.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^2];
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static string? ReadHref(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.LocalName == "href")
            {
                return attribute.Value;
            }
        }

        return null;
    }

    private static string? ReadAttribute(XElement element, string name, SvgRenderState state)
    {
        var attribute = (string?)element.Attribute(name);
        if (!string.IsNullOrWhiteSpace(attribute))
        {
            return attribute;
        }

        var styleValue = ReadStyleValue(element, name);
        return string.IsNullOrWhiteSpace(styleValue)
            ? state.ReadInheritedValue(name)
            : styleValue;
    }

    private static string? ReadStyleValue(XElement element, string name)
    {
        var style = (string?)element.Attribute("style");
        if (string.IsNullOrWhiteSpace(style))
        {
            return null;
        }

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = declaration.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var key = declaration[..separator].Trim();
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return declaration[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    private static bool TryParseUrlReference(string value, out string id)
    {
        id = string.Empty;
        value = value.Trim();
        const string prefix = "url(#";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !value.EndsWith(')'))
        {
            return false;
        }

        id = value[prefix.Length..^1];
        return id.Length > 0;
    }

    private static bool TryParseTransform(string transform, out Matrix matrix)
    {
        matrix = Matrix.Identity;
        var remaining = transform.AsSpan().Trim();
        while (!remaining.IsEmpty)
        {
            var open = remaining.IndexOf('(');
            var close = remaining.IndexOf(')');
            if (open <= 0 || close <= open)
            {
                return false;
            }

            var name = remaining[..open].Trim().ToString();
            var values = ParseNumberList(remaining[(open + 1)..close].ToString());
            if (!TryCreateTransformMatrix(name, values, out var next))
            {
                return false;
            }

            matrix *= next;
            remaining = remaining[(close + 1)..].Trim();
        }

        return true;
    }

    private static bool TryCreateTransformMatrix(string name, double[] values, out Matrix matrix)
    {
        matrix = Matrix.Identity;
        switch (name)
        {
            case "matrix" when values.Length == 6:
                matrix = new Matrix(values[0], values[1], values[2], values[3], values[4], values[5]);
                return true;
            case "translate" when values.Length >= 1:
                matrix = Matrix.CreateTranslation(values[0], values.Length >= 2 ? values[1] : 0);
                return true;
            case "scale" when values.Length >= 1:
                matrix = Matrix.CreateScale(values[0], values.Length >= 2 ? values[1] : values[0]);
                return true;
            default:
                return false;
        }
    }

    private static double[] ParseNumberList(string value)
    {
        return value
            .Split([' ', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => double.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private sealed record SvgRenderState(
        double Opacity,
        Color? Fill,
        double FillOpacity,
        Color? Stroke,
        double StrokeOpacity,
        double StrokeWidth,
        string TextAnchor)
    {
        public static SvgRenderState Default { get; } = new(
            Opacity: 1,
            Fill: Colors.Black,
            FillOpacity: 1,
            Stroke: null,
            StrokeOpacity: 1,
            StrokeWidth: 1,
            TextAnchor: "start");

        public SvgRenderState Inherit(XElement element)
        {
            var opacity = Opacity * ReadOpacity(element, "opacity", 1);
            var fill = ReadColor(element, "fill", Fill);
            var stroke = ReadColor(element, "stroke", Stroke);
            return this with
            {
                Opacity = opacity,
                Fill = fill,
                FillOpacity = FillOpacity * ReadOpacity(element, "fill-opacity", 1),
                Stroke = stroke,
                StrokeOpacity = StrokeOpacity * ReadOpacity(element, "stroke-opacity", 1),
                StrokeWidth = ReadDouble(element, "stroke-width", StrokeWidth, this),
                TextAnchor = ReadTextAnchor(element, TextAnchor)
            };
        }

        public string? ReadInheritedValue(string name)
        {
            return name switch
            {
                "fill" => Fill?.ToString(),
                "stroke" => Stroke?.ToString(),
                "stroke-width" => StrokeWidth.ToString(CultureInfo.InvariantCulture),
                "fill-opacity" => FillOpacity.ToString(CultureInfo.InvariantCulture),
                "stroke-opacity" => StrokeOpacity.ToString(CultureInfo.InvariantCulture),
                "opacity" => Opacity.ToString(CultureInfo.InvariantCulture),
                "text-anchor" => TextAnchor,
                _ => null
            };
        }

        public IBrush? CreateFillBrush()
        {
            return Fill is null
                ? null
                : new SolidColorBrush(Fill.Value, Opacity * FillOpacity);
        }

        public IPen? CreatePen()
        {
            return Stroke is null
                ? null
                : new Pen(new SolidColorBrush(Stroke.Value, Opacity * StrokeOpacity), StrokeWidth);
        }

        private static double ReadOpacity(XElement element, string name, double defaultValue)
        {
            var value = ReadLocalAttribute(element, name);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : defaultValue;
        }

        private static string ReadTextAnchor(XElement element, string defaultValue)
        {
            var value = ReadLocalAttribute(element, "text-anchor");
            return string.IsNullOrWhiteSpace(value)
                ? defaultValue
                : value.Trim();
        }

        private static Color? ReadColor(XElement element, string name, Color? defaultValue)
        {
            var value = ReadLocalAttribute(element, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            value = value.Trim();
            if (string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return TryParseColor(value, out var color)
                ? color
                : defaultValue;
        }

        private static string? ReadLocalAttribute(XElement element, string name)
        {
            var attribute = (string?)element.Attribute(name);
            return string.IsNullOrWhiteSpace(attribute)
                ? ReadStyleValue(element, name)
                : attribute;
        }

        private static bool TryParseColor(string value, out Color color)
        {
            value = value.Trim();
            if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && value.EndsWith(')'))
            {
                var parts = value[4..^1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3)
                {
                    color = Color.FromRgb(
                        ParseColorChannel(parts[0]),
                        ParseColorChannel(parts[1]),
                        ParseColorChannel(parts[2]));
                    return true;
                }
            }

            return Color.TryParse(value, out color);
        }

        private static byte ParseColorChannel(string value)
        {
            value = value.Trim();
            var multiplier = 1.0;
            if (value.EndsWith('%'))
            {
                value = value[..^1];
                multiplier = 255.0 / 100.0;
            }

            var parsed = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture) * multiplier;
            return (byte)Math.Clamp((int)Math.Round(parsed), 0, 255);
        }
    }
}

internal readonly record struct SvgMaskAnalysis(
    int GroupCount,
    bool HasTransparentPixels,
    bool HasOpaquePixels);
