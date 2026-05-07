var createApp = require('../tiny-express/tiny-express');
var createJsonDb = require('./json-db');
var createStaticFiles = require('./static-files');

var app = createApp();
var db = createJsonDb('data/issues.json');
var staticFiles = createStaticFiles('public');

app.get('/', function (_, res) {
  staticFiles.send(res, 'index.html');
});

app.get('/assets/:file', function (req, res) {
  if (!staticFiles.send(res, req.params.file)) {
    res.status(404).json({ error: 'asset_not_found', file: req.params.file });
  }
});

app.get('/api/status', function (_, res) {
  res.json({
    engine: 'Asynkron.JsEngine',
    runtime: 'MiniNodeRuntime',
    app: 'issue-tracker',
    uptimeSeconds: process.uptime(),
    issueCount: db.all().length
  });
});

app.get('/api/issues', function (req, res) {
  var issues = db.all();
  var result = [];

  for (var i = 0; i < issues.length; i++) {
    var issue = issues[i];
    if (req.query.status && issue.status !== req.query.status) {
      continue;
    }

    if (req.query.owner && issue.owner !== req.query.owner) {
      continue;
    }

    result.push(issue);
  }

  res.json({ count: result.length, issues: result });
});

app.get('/api/issues/:id', function (req, res) {
  var issue = db.get(req.params.id);
  if (!issue) {
    res.status(404).json({ error: 'issue_not_found', id: req.params.id });
    return;
  }

  res.json(issue);
});

app.post('/api/issues', function (req, res) {
  var input = readJson(req, res);
  if (!input) {
    return;
  }

  if (!input.title || String(input.title).trim() === '') {
    res.status(422).json({ error: 'title_required' });
    return;
  }

  var created = db.create({
    title: String(input.title),
    owner: input.owner ? String(input.owner) : 'unassigned',
    status: input.status ? String(input.status) : 'open',
    priority: input.priority ? String(input.priority) : 'medium'
  });

  res.status(201).json(created);
});

app.patch('/api/issues/:id', function (req, res) {
  var input = readJson(req, res);
  if (!input) {
    return;
  }

  var updated = db.update(req.params.id, input);
  if (!updated) {
    res.status(404).json({ error: 'issue_not_found', id: req.params.id });
    return;
  }

  res.json(updated);
});

function readJson(req, res) {
  if (!req.body || req.body === '') {
    return {};
  }

  try {
    return JSON.parse(req.body);
  } catch (_) {
    res.status(400).json({ error: 'invalid_json' });
    return null;
  }
}

app.listen(9615);
