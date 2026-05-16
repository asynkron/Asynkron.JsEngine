# Avalonia SVG Browser Demo

This example is a JsEngine-powered presentation viewer. Avalonia hosts the
window and SVG surface; JsEngine runs `scripts/presentation.js`, which controls
navigation, slide transitions, and SVG overlays through a small native bridge.

Run it:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo
```

You can also start on a specific page:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo -- --presentation 12
```

The converted presentation assets live in `assets/beyond-the-vibe/`:

- `pages/` contains the SVG pages.
- `images/` contains the extracted image streams from the PDF conversion.

The host bridge exposed to JavaScript is intentionally custom, not a browser
DOM:

- `presentation.count()`
- `presentation.path(index)`
- `presentation.current()`
- `presentation.load(indexOrFileName)`
- `slideScript(fileName, hooks)`
- `slide.onFrame(callback)`
- `slide.onKey(key, callback)`
- `svg.id(id).set(name, value)`
- `svg.id(id).text(value)`
- `svg.id(id).transform(value)`
- `svg.layer.clear()`
- `svg.layer.rect(id, x, y, width, height, fill, opacity)`
- `svg.layer.circle(id, cx, cy, r, fill, opacity)`
- `svg.layer.path(id, d, fill, stroke, strokeWidth, opacity)`
- `svg.layer.text(id, text, x, y, size, fill, opacity)`

`presentation.js` owns the slide order with an explicit `slides[index] =
"file-name.svg"` list, handles `ArrowLeft`, `ArrowRight`, `Home`, and `End`,
draws a JS-owned overlay layer, and runs a fade transition entirely from
JsEngine callbacks. On startup, the host preloads the deck's image assets and
generated SVG image-mask composites so navigation does not block on alpha-heavy
slides.

Per-slide sidecars live in `scripts/slides/*.js`. The host evaluates
`presentation.js`, then wraps and evaluates each sidecar, then starts the deck.
Sidecars register lifecycle hooks:

```javascript
slideScript("ecma-262-94000-unit-tests.svg", {
  enter: function (ctx) {},
  frame: function (ctx, time, elapsed) {},
  leave: function (ctx) {},
  key: function (ctx, key) {}
});
```

Returning `true` from `key` consumes that key for the current slide.

Smoke-test the JS navigation without opening a window:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo -- --presentation-smoke
```

Smoke-test the generated SVG image-mask handling used by the angel slide:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo -- --presentation-mask-smoke
```

Smoke-test per-slide sidecar registration and animation hooks:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo -- --presentation-sidecar-smoke
```

Smoke-test startup preloading without opening the window:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo -- --presentation-preload-smoke
```
