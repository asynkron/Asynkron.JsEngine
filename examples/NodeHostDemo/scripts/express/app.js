var express = require('express');

var app = express();
var startedAt = new Date().toISOString();
var auditTrail = [];
var nextRequestId = 1;

app.use(assignRequestId);
app.use(measureResponseTime);
app.use(recordAuditEntry);
app.use(cors);
app.use('/api', parseJsonBodyFromHost);

app.get('/', function (_, res) {
  res.type('text/plain');
  res.send('Hello from real Express middleware running on Asynkron.JsEngine\n');
});

app.get('/api/status', function (req, res) {
  res.json({
    framework: 'express',
    engine: 'Asynkron.JsEngine',
    startedAt: startedAt,
    uptimeSeconds: process.uptime(),
    middleware: [
      'assignRequestId',
      'measureResponseTime',
      'recordAuditEntry',
      'cors',
      'parseJsonBodyFromHost'
    ],
    requestId: req.id
  });
});

app.get('/api/hello/:name', function (req, res) {
  res.json({
    hello: req.params.name,
    query: req.query,
    requestId: req.id
  });
});

app.post('/api/echo', function (req, res) {
  res.status(201).json({
    requestId: req.id,
    body: req.parsedBody || {}
  });
});

app.get('/api/audit', function (req, res) {
  res.json({
    requestId: req.id,
    entries: auditTrail
  });
});

app.use(function notFound(req, res) {
  res.status(404).json({
    error: 'not_found',
    method: req.method,
    url: req.url,
    requestId: req.id || null
  });
});

app.listen(9615);

function assignRequestId(req, res, next) {
  req.id = 'req-' + nextRequestId++;
  req.startedAt = Date.now();
  res.setHeader('X-Request-Id', req.id);
  next();
}

function measureResponseTime(req, res, next) {
  var end = res.end;
  res.end = function (body) {
    res.setHeader('X-Response-Time', Date.now() - req.startedAt + 'ms');
    end.call(res, body);
  };
  next();
}

function cors(req, res, next) {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET,POST,OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type,X-Request-Id');

  if (req.method === 'OPTIONS') {
    res.status(204).end('');
    return;
  }

  next();
}

function parseJsonBodyFromHost(req, res, next) {
  var contentType = req.headers['content-type'] || '';
  req.parsedBody = null;

  if (req.body && contentType.indexOf('application/json') !== -1) {
    try {
      req.parsedBody = JSON.parse(req.body);
    } catch (_) {
      res.status(400).json({
        error: 'invalid_json',
        requestId: req.id
      });
      return;
    }
  }

  next();
}

function recordAuditEntry(req, res, next) {
  var end = res.end;
  res.end = function (body) {
    auditTrail.push({
      id: req.id,
      method: req.method,
      url: req.originalUrl || req.url,
      statusCode: res.statusCode || 200,
      durationMs: Date.now() - req.startedAt
    });

    if (auditTrail.length > 20) {
      auditTrail.shift();
    }

    end.call(res, body);
  };

  next();
}
