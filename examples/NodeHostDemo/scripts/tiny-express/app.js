var createApp = require('./tiny-express');
var fs = require('fs');

var app = createApp();
var db = JSON.parse(fs.readFileSync('data.json'));

function findTodo(id) {
  var numericId = Number(id);
  for (var i = 0; i < db.todos.length; i++) {
    if (db.todos[i].id === numericId) {
      return db.todos[i];
    }
  }
  return null;
}

function todosForOwner(owner) {
  var result = [];
  for (var i = 0; i < db.todos.length; i++) {
    if (db.todos[i].owner === owner) {
      result.push(db.todos[i]);
    }
  }
  return result;
}

app.get('/', function (req, res) {
  var openCount = 0;
  for (var i = 0; i < db.todos.length; i++) {
    if (!db.todos[i].done) {
      openCount++;
    }
  }

  res.html([
    '<!doctype html>',
    '<html>',
    '<head><title>JsEngine Tiny Express</title></head>',
    '<body>',
    '<h1>JsEngine Tiny Express</h1>',
    '<p>Routes: ' + app.routes.length + '</p>',
    '<p>Todos: ' + db.todos.length + ' total, ' + openCount + ' open</p>',
    '<ul>',
    '<li><a href="/api/status">/api/status</a></li>',
    '<li><a href="/api/todos">/api/todos</a></li>',
    '<li><a href="/api/todos/3">/api/todos/3</a></li>',
    '<li><a href="/api/users/ada/todos">/api/users/ada/todos</a></li>',
    '</ul>',
    '</body>',
    '</html>'
  ].join(''));
});

app.get('/api/status', function (req, res) {
  res.json({
    engine: 'Asynkron.JsEngine',
    runtime: 'MiniNodeRuntime',
    uptimeSeconds: process.uptime(),
    routeCount: app.routes.length
  });
});

app.get('/api/todos', function (req, res) {
  var owner = req.query.owner;
  res.json({
    count: owner ? todosForOwner(owner).length : db.todos.length,
    todos: owner ? todosForOwner(owner) : db.todos
  });
});

app.get('/api/todos/:id', function (req, res) {
  var todo = findTodo(req.params.id);
  if (!todo) {
    res.status(404).json({ error: 'todo_not_found', id: req.params.id });
    return;
  }

  res.json(todo);
});

app.get('/api/users/:owner/todos', function (req, res) {
  res.json({
    owner: req.params.owner,
    todos: todosForOwner(req.params.owner)
  });
});

app.listen(9615);
