function createApp() {
  var routes = [];

  function app(req, res) {
    var parsed = parseUrl(req.url || '/');
    var route = matchRoute(req.method, parsed.pathname);

    res.status = function (code) {
      res.statusCode = code;
      return res;
    };

    res.type = function (contentType) {
      res.contentType = contentType;
      return res;
    };

    res.send = function (body) {
      var statusCode = res.statusCode || 200;
      var contentType = res.contentType || 'text/plain';
      res.writeHead(statusCode, { 'Content-Type': contentType });
      res.end(String(body));
    };

    res.html = function (body) {
      res.type('text/html; charset=utf-8');
      res.send(body);
    };

    res.json = function (value) {
      res.writeHead(res.statusCode || 200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(value));
    };

    if (!route) {
      res.status(404).json({
        error: 'not_found',
        method: req.method,
        path: parsed.pathname
      });
      return;
    }

    req.path = parsed.pathname;
    req.query = parsed.query;
    req.params = route.params;
    route.handler(req, res);
  }

  app.routes = routes;

  app.get = function (pattern, handler) {
    return addRoute('GET', pattern, handler);
  };

  app.post = function (pattern, handler) {
    return addRoute('POST', pattern, handler);
  };

  app.patch = function (pattern, handler) {
    return addRoute('PATCH', pattern, handler);
  };

  function addRoute(method, pattern, handler) {
    routes.push({
      method: method,
      pattern: pattern,
      parts: splitPath(pattern),
      handler: handler
    });
    return app;
  }

  app.listen = function (port) {
    return require('http').createServer(app).listen(port);
  };

  function matchRoute(method, pathname) {
    var parts = splitPath(pathname);

    for (var i = 0; i < routes.length; i++) {
      var route = routes[i];
      if (route.method !== method || route.parts.length !== parts.length) {
        continue;
      }

      var params = {};
      var matched = true;
      for (var j = 0; j < route.parts.length; j++) {
        var expected = route.parts[j];
        var actual = parts[j];
        if (expected.charAt(0) === ':') {
          params[expected.slice(1)] = decodeURIComponent(actual);
        } else if (expected !== actual) {
          matched = false;
          break;
        }
      }

      if (matched) {
        return { handler: route.handler, params: params };
      }
    }

    return null;
  }

  return app;
}

function parseUrl(url) {
  var queryIndex = url.indexOf('?');
  var pathname = queryIndex === -1 ? url : url.slice(0, queryIndex);
  var queryString = queryIndex === -1 ? '' : url.slice(queryIndex + 1);
  var query = {};

  if (queryString.length > 0) {
    var pairs = queryString.split('&');
    for (var i = 0; i < pairs.length; i++) {
      if (pairs[i] === '') {
        continue;
      }

      var equalsIndex = pairs[i].indexOf('=');
      var key = equalsIndex === -1 ? pairs[i] : pairs[i].slice(0, equalsIndex);
      var value = equalsIndex === -1 ? '' : pairs[i].slice(equalsIndex + 1);
      query[decodeURIComponent(key)] = decodeURIComponent(value.replace(/\+/g, ' '));
    }
  }

  return { pathname: pathname || '/', query: query };
}

function splitPath(pathname) {
  var rawParts = pathname.split('/');
  var parts = [];
  for (var i = 0; i < rawParts.length; i++) {
    if (rawParts[i] !== '') {
      parts.push(rawParts[i]);
    }
  }
  return parts;
}

module.exports = createApp;
