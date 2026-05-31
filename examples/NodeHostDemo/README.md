# Node Host Demo

This example is a deliberately small Node-shaped host for Asynkron.JsEngine.
It is not a Node.js clone; it is a C# console app that embeds `JsEngine`,
loads a JavaScript file, and exposes native .NET host modules through
`require`.

Run the first script:

```bash
rtk dotnet run --project examples/NodeHostDemo
```

Then open:

```bash
rtk curl http://localhost:9615/
```

The initial module surface is intentionally tiny:

- `require('fs').readFileSync(path)`
- `require('http').createServer(callback).listen(port)`
- response methods `writeHead(statusCode, headers)` and `end(body)`
- request properties `method`, `url`, and `headers`

Run the larger tiny-Express-style app:

```bash
rtk dotnet run --project examples/NodeHostDemo -- examples/NodeHostDemo/scripts/tiny-express/app.js
```

Then try:

```bash
rtk curl http://localhost:9615/
rtk curl http://localhost:9615/api/status
rtk curl http://localhost:9615/api/todos
rtk curl http://localhost:9615/api/todos/3
rtk curl http://localhost:9615/api/users/ada/todos
```

This second script adds a JavaScript framework layer with route matching,
params, query parsing, `res.send`, `res.html`, `res.json`, relative
`require('./tiny-express')`, `module.exports`, and `process.uptime()`.

Run the issue tracker app:

```bash
rtk dotnet run --project examples/NodeHostDemo -- examples/NodeHostDemo/scripts/issue-tracker/app.js
```

Then try the browser UI and JSON API:

```bash
rtk curl http://localhost:9615/
rtk curl http://localhost:9615/assets/app.js
rtk curl http://localhost:9615/api/status
rtk curl http://localhost:9615/api/issues
rtk curl http://localhost:9615/api/issues/2
rtk curl -X POST http://localhost:9615/api/issues \
  -H 'Content-Type: application/json' \
  -d '{"title":"Try POST bodies","owner":"ada","priority":"high"}'
rtk curl -X PATCH http://localhost:9615/api/issues/2 \
  -H 'Content-Type: application/json' \
  -d '{"status":"closed"}'
```

This third script adds multiple local JavaScript modules, a static browser UI,
`POST` and `PATCH` routes, request bodies, a native `path` module, and JSON
persistence through host-backed `fs.writeFileSync`.

Run the real Express package app:

```bash
rtk npm --prefix examples/NodeHostDemo install
rtk npm --prefix examples/NodeHostDemo run express
```

Then try:

```bash
rtk curl http://localhost:9615/
rtk curl http://localhost:9615/api/status
rtk curl 'http://localhost:9615/api/hello/roger?x=1'
rtk curl -i -X OPTIONS http://localhost:9615/api/echo
rtk curl -X POST http://localhost:9615/api/echo \
  -H 'Content-Type: application/json' \
  -d '{"message":"hello from middleware"}'
rtk curl -X POST http://localhost:9615/api/echo \
  -H 'Content-Type: application/json' \
  -d '{bad json'
rtk curl http://localhost:9615/not-found
rtk curl http://localhost:9615/api/audit
```

This fourth script loads the real `express` npm package from `node_modules`
and demonstrates real Express middleware with `app.use(...)`: request IDs,
request logging through the host-backed `console`, response timing,
CORS/preflight handling, host-backed JSON body parsing, an in-memory audit
trail, and 404 handling.

Run an unchanged example from the official Express repository:

```bash
rtk npm --prefix examples/NodeHostDemo run prepare:official-express-ejs
rtk npm --prefix examples/NodeHostDemo run official-express-ejs
```

Then try:

```bash
rtk curl http://localhost:3000/
rtk curl http://localhost:3000/stylesheets/style.css
```

This script clones [expressjs/express](https://github.com/expressjs/express)
into `examples/NodeHostDemo/third_party/express`, installs that repository's
dependencies, and runs `examples/ejs/index.js` unchanged through JsEngine. It
exercises the real Express app, router, `express.static(...)`, EJS view
rendering, nested package resolution, `node:` built-in aliases, async
`fs.stat`, static file streaming, `TextDecoder`, and Node-shaped
request/response host methods.

Run the real Polka package app:

```bash
rtk npm --prefix examples/NodeHostDemo install
rtk npm --prefix examples/NodeHostDemo run polka
```

Then try:

```bash
rtk curl http://localhost:9615/
rtk curl http://localhost:9615/api/status
rtk curl 'http://localhost:9615/api/hello/roger?x=1'
```

This fifth script loads `polka` from `node_modules` through the CommonJS
resolver and runs the framework code unchanged. The host still only supplies
the native edges: package resolution, `http`, `querystring`, and enough
request/response behavior for Polka's router.

## Dependency baseline note (2026-05-28 signal)

The real Express package demo now uses the current stable Express line. On
2026-05-28, `npm view express version dist-tags.latest` reported `5.2.1`.
Issue #2445 / PR #2457 updated `package.json` to `^5.2.1` and refreshed
`package-lock.json` to resolve `express` at `5.2.1`.

Express remains the framework compatibility surface for this demo, so future
Express updates should keep `package.json`, `package-lock.json`, and this note
aligned. After changing Express, smoke at least `/api/status` and a
parameterized route such as `/api/hello/agent?from=smoke` because routing and
middleware behavior are the observable risk.

Polka is current on its stable npm tag: `polka@0.5.2` is both the installed
range target and `latest`. On the same 2026-05-27 signal,
`npm view polka version dist-tags.latest dist-tags.next` reported `next` as
`1.0.0-next.28`, so this demo should not move to Polka 1.x without an explicit
pre-release compatibility pass.

Future scripts can grow this folder step by step without changing the core
engine: add more host modules, more request/response behavior, and eventually
small packages that expect common Node globals.
