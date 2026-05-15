using System.Globalization;
using System.Xml.Linq;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace AvaloniaSvgBrowserDemo;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect();
    }
}

internal sealed class App : Application
{
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new DemoWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal sealed class DemoWindow : Window
{
    private readonly SvgSlideView _svgView;
    private readonly SvgSlideDocument _document;
    private readonly SlideScriptHost _scriptHost;
    private readonly DispatcherTimer _timer;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public DemoWindow()
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
        var svgPath = Path.Combine(baseDirectory, "assets", "slide.svg");
        var scriptPath = Path.Combine(baseDirectory, "scripts", "slide.js");
        var renderedPath = Path.Combine(baseDirectory, "rendered-slide.svg");

        _document = new SvgSlideDocument(svgPath, renderedPath, RenderSvg);
        _svgView.Document = _document;
        _document.Flush();

        Content = CreateLayout();

        _scriptHost = new SlideScriptHost(_document);
        _scriptHost.Run(scriptPath);

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
        Closed += (_, _) => _scriptHost.Dispose();
    }

    private Control CreateLayout()
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

        var status = new TextBlock
        {
            Text = "JsEngine is animating this SVG. Press Space, then R.",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(210, 5, 7, 13)),
            Padding = new Thickness(14, 8),
            FontSize = 15,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetRow(status, 1);
        root.Children.Add(status);

        return root;
    }

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

    private static string NormalizeKey(KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Space)
        {
            return "Space";
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
    private readonly Dictionary<string, List<IJsCallable>> _keyCallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly SvgSlideDocument _document;

    public SlideScriptHost(SvgSlideDocument document)
    {
        _document = document;
        _engine.SetGlobalValue("console", CreateConsoleObject());
        _engine.SetGlobalValue("svg", CreateSvgObject());
        _engine.SetGlobalValue("slide", CreateSlideObject());
    }

    public void Run(string scriptPath)
    {
        var script = File.ReadAllText(scriptPath);
        _engine.EvaluateSync(script);
        _document.Flush();
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

    public void Dispose()
    {
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
        return slide;
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

internal sealed class SvgSlideDocument
{
    private readonly XDocument _document;
    private readonly Action _render;
    private bool _dirty;
    private double _viewBoxWidth = 1280;
    private double _viewBoxHeight = 720;
    public SvgSlideDocument(string sourcePath, string renderedPath, Action render)
    {
        _document = XDocument.Load(sourcePath, LoadOptions.PreserveWhitespace);
        RenderedPath = renderedPath;
        _render = render;
        _dirty = true;
    }

    public string RenderedPath { get; }

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

    public void Flush()
    {
        if (!_dirty)
        {
            return;
        }

        _document.Save(RenderedPath, SaveOptions.DisableFormatting);
        ReadViewBox();
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
            foreach (var child in _document.Root.Elements())
            {
                RenderElement(context, child, inheritedOpacity: 1.0);
            }
        }
    }

    private XElement GetElementById(string id)
    {
        var element = _document
            .Descendants()
            .FirstOrDefault(candidate =>
                string.Equals((string?)candidate.Attribute("id"), id, StringComparison.Ordinal));

        if (element is null)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"SVG element not found: {id}"));
        }

        return element;
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
            _viewBoxWidth = ReadDouble(_document.Root, "width", _viewBoxWidth);
            _viewBoxHeight = ReadDouble(_document.Root, "height", _viewBoxHeight);
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

    private static void RenderElement(DrawingContext context, XElement element, double inheritedOpacity)
    {
        var opacity = inheritedOpacity * ReadDouble(element, "opacity", 1.0);
        if (opacity <= 0)
        {
            return;
        }

        var localName = element.Name.LocalName;
        if (localName == "g")
        {
            using var transform = PushElementTransform(context, element);
            foreach (var child in element.Elements())
            {
                RenderElement(context, child, opacity);
            }

            return;
        }

        using var _ = PushElementTransform(context, element);
        switch (localName)
        {
            case "rect":
                RenderRect(context, element, opacity);
                break;
            case "circle":
                RenderCircle(context, element, opacity);
                break;
            case "path":
                RenderPath(context, element, opacity);
                break;
            case "text":
                RenderText(context, element, opacity);
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

        if (TryParseTranslate(transform, out var x, out var y))
        {
            return context.PushTransform(Matrix.CreateTranslation(x, y));
        }

        return null;
    }

    private static void RenderRect(DrawingContext context, XElement element, double opacity)
    {
        var x = ReadDouble(element, "x", 0);
        var y = ReadDouble(element, "y", 0);
        var width = ReadDouble(element, "width", 0);
        var height = ReadDouble(element, "height", 0);
        var rx = ReadDouble(element, "rx", 0);
        var fill = ReadBrush(element, "fill", opacity);
        var pen = ReadPen(element, opacity);
        context.DrawRectangle(fill, pen, new Rect(x, y, width, height), rx, rx);
    }

    private static void RenderCircle(DrawingContext context, XElement element, double opacity)
    {
        var cx = ReadDouble(element, "cx", 0);
        var cy = ReadDouble(element, "cy", 0);
        var r = ReadDouble(element, "r", 0);
        var fill = ReadBrush(element, "fill", opacity);
        var pen = ReadPen(element, opacity);
        context.DrawEllipse(fill, pen, new Point(cx, cy), r, r);
    }

    private static void RenderPath(DrawingContext context, XElement element, double opacity)
    {
        var d = (string?)element.Attribute("d");
        if (string.IsNullOrWhiteSpace(d))
        {
            return;
        }

        var geometry = Geometry.Parse(d);
        var fill = ReadBrush(element, "fill", opacity);
        var pen = ReadPen(element, opacity);
        context.DrawGeometry(fill, pen, geometry);
    }

    private static void RenderText(DrawingContext context, XElement element, double opacity)
    {
        var x = ReadDouble(element, "x", 0);
        var y = ReadDouble(element, "y", 0);
        var fontSize = ReadDouble(element, "font-size", 20);
        var brush = ReadBrush(element, "fill", opacity) ?? Brushes.White;
        var formatted = new FormattedText(
            element.Value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            fontSize,
            brush);

        context.DrawText(formatted, new Point(x, y - fontSize));
    }

    private static IBrush? ReadBrush(XElement element, string attributeName, double opacity)
    {
        var value = (string?)element.Attribute(attributeName);
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Color.TryParse(value, out var color)
            ? new SolidColorBrush(color, opacity)
            : null;
    }

    private static IPen? ReadPen(XElement element, double opacity)
    {
        var stroke = ReadBrush(element, "stroke", opacity);
        if (stroke is null)
        {
            return null;
        }

        var width = ReadDouble(element, "stroke-width", 1);
        return new Pen(stroke, width);
    }

    private static double ReadDouble(XElement element, string name, double defaultValue)
    {
        var value = (string?)element.Attribute(name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static bool TryParseTranslate(string transform, out double x, out double y)
    {
        x = 0;
        y = 0;
        const string prefix = "translate(";
        if (!transform.StartsWith(prefix, StringComparison.Ordinal) || !transform.EndsWith(')'))
        {
            return false;
        }

        var inner = transform[prefix.Length..^1];
        var parts = inner.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x))
        {
            return false;
        }

        if (parts.Length > 1)
        {
            _ = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y);
        }

        return true;
    }
}
