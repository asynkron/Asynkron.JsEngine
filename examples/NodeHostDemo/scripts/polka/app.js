var polka = require('polka');

var app = polka();
var startedAt = new Date().toISOString();

app.get('/', function (_, res) {
  res.setHeader('Content-Type', 'text/plain; charset=utf-8');
  res.end('Hello from real Polka running on Asynkron.JsEngine\n');
});

app.get('/api/status', function (_, res) {
  sendJson(res, {
    framework: 'polka',
    engine: 'Asynkron.JsEngine',
    startedAt: startedAt,
    uptimeSeconds: process.uptime()
  });
});

app.get('/api/hello/:name', function (req, res) {
  sendJson(res, {
    hello: req.params.name,
    query: req.query
  });
});

app.listen(9615);

function sendJson(res, value) {
  res.setHeader('Content-Type', 'application/json; charset=utf-8');
  res.end(JSON.stringify(value));
}
