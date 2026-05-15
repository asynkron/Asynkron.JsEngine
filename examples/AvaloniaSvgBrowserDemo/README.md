# Avalonia SVG Browser Demo

This example is the first slice of a JsEngine-powered presentation viewer.
It is intentionally not a real browser. Avalonia hosts the window and SVG
surface, while JsEngine runs a slide script that mutates SVG elements through
a tiny custom API.

Run it:

```bash
dotnet run --project examples/AvaloniaSvgBrowserDemo
```

The demo loads `assets/slide.svg`, runs `scripts/slide.js`, and exposes:

- `svg.id(id).set(name, value)`
- `svg.id(id).text(value)`
- `svg.id(id).transform(value)`
- `slide.onFrame(callback)`
- `slide.onKey(key, callback)`
- `console.log(...)`

Press `Space` to let JsEngine change the headline and subtitle. Press `R` to
reset the text.
