# Node Host Demo

This example is a deliberately small Node-shaped host for Asynkron.JsEngine.
It is not a Node.js clone; it is a C# console app that embeds `JsEngine`,
loads a JavaScript file, and exposes native .NET host modules through
`require`.

Run the first script:

```bash
dotnet run --project examples/NodeHostDemo
```

Then open:

```bash
curl http://localhost:9615/
```

The initial module surface is intentionally tiny:

- `require('fs').readFileSync(path)`
- `require('http').createServer(callback).listen(port)`
- response methods `writeHead(statusCode, headers)` and `end(body)`
- request properties `method`, `url`, and `headers`

Future scripts can grow this folder step by step without changing the core
engine: add more host modules, more request/response behavior, and eventually
small packages that expect common Node globals.
