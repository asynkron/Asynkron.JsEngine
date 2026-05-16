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
- `presentation.load(index)`
- `slide.onFrame(callback)`
- `slide.onKey(key, callback)`
- `svg.id(id).set(name, value)`
- `svg.id(id).text(value)`
- `svg.id(id).transform(value)`
- `svg.layer.clear()`
- `svg.layer.rect(id, x, y, width, height, fill, opacity)`
- `svg.layer.circle(id, cx, cy, r, fill, opacity)`
- `svg.layer.text(id, text, x, y, size, fill, opacity)`

`presentation.js` builds its own `slides` array from the host bridge, handles
`ArrowLeft`, `ArrowRight`, `Home`, and `End`, draws a JS-owned overlay layer,
and runs a fade transition entirely from JsEngine callbacks.

Smoke-test the JS navigation without opening a window:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo -- --presentation-smoke
```

Smoke-test the generated SVG image-mask handling used by the angel slide:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo -- --presentation-mask-smoke
```
