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

Run the larger tiny-Express-style app:

```bash
dotnet run --project examples/NodeHostDemo -- examples/NodeHostDemo/scripts/tiny-express/app.js
```

Then try:

```bash
curl http://localhost:9615/
curl http://localhost:9615/api/status
curl http://localhost:9615/api/todos
curl http://localhost:9615/api/todos/3
curl http://localhost:9615/api/users/ada/todos
```

This second script adds a JavaScript framework layer with route matching,
params, query parsing, `res.send`, `res.html`, `res.json`, relative
`require('./tiny-express')`, `module.exports`, and `process.uptime()`.

Run the issue tracker app:

```bash
dotnet run --project examples/NodeHostDemo -- examples/NodeHostDemo/scripts/issue-tracker/app.js
```

Then try the browser UI and JSON API:

```bash
curl http://localhost:9615/
curl http://localhost:9615/assets/app.js
curl http://localhost:9615/api/status
curl http://localhost:9615/api/issues
curl http://localhost:9615/api/issues/2
curl -X POST http://localhost:9615/api/issues \
  -H 'Content-Type: application/json' \
  -d '{"title":"Try POST bodies","owner":"ada","priority":"high"}'
curl -X PATCH http://localhost:9615/api/issues/2 \
  -H 'Content-Type: application/json' \
  -d '{"status":"closed"}'
```

This third script adds multiple local JavaScript modules, a static browser UI,
`POST` and `PATCH` routes, request bodies, a native `path` module, and JSON
persistence through host-backed `fs.writeFileSync`.

Run the real Express package app:

```bash
cd examples/NodeHostDemo
npm install
npm run express
```

Then try:

```bash
curl http://localhost:9615/
curl http://localhost:9615/api/status
curl 'http://localhost:9615/api/hello/roger?x=1'
curl -i -X OPTIONS http://localhost:9615/api/echo
curl -X POST http://localhost:9615/api/echo \
  -H 'Content-Type: application/json' \
  -d '{"message":"hello from middleware"}'
curl -X POST http://localhost:9615/api/echo \
  -H 'Content-Type: application/json' \
  -d '{bad json'
curl http://localhost:9615/not-found
curl http://localhost:9615/api/audit
```

This fourth script loads the real `express` npm package from `node_modules`
and demonstrates real Express middleware with `app.use(...)`: request IDs,
response timing, CORS/preflight handling, host-backed JSON body parsing, an
in-memory audit trail, and 404 handling.

Run the real Polka package app:

```bash
cd examples/NodeHostDemo
npm install
npm run polka
```

Then try:

```bash
curl http://localhost:9615/
curl http://localhost:9615/api/status
curl 'http://localhost:9615/api/hello/roger?x=1'
```

This fifth script loads `polka` from `node_modules` through the CommonJS
resolver and runs the framework code unchanged. The host still only supplies
the native edges: package resolution, `http`, `querystring`, and enough
request/response behavior for Polka's router.

Future scripts can grow this folder step by step without changing the core
engine: add more host modules, more request/response behavior, and eventually
small packages that expect common Node globals.
