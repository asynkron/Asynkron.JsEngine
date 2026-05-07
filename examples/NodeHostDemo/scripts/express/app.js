var express = require('express');

var app = express();
var startedAt = new Date().toISOString();

app.get('/', function (_, res) {
  res.type('text/plain');
  res.send('Hello from real Express running on Asynkron.JsEngine\n');
});

app.get('/hello/:name', function (req, res) {
  res.json({
    hello: req.params.name,
    query: req.query,
    startedAt: startedAt
  });
});

app.listen(9615);
