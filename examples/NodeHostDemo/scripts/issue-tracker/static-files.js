var fs = require('fs');
var path = require('path');

function createStaticFiles(root) {
  return {
    send: function (res, file) {
      if (!isSafeFile(file)) {
        return false;
      }

      var filePath = path.join(root, file);
      if (!fs.existsSync(filePath)) {
        return false;
      }

      res.type(contentType(file));
      res.send(fs.readFileSync(filePath));
      return true;
    }
  };
}

function isSafeFile(file) {
  return file &&
    file.indexOf('..') === -1 &&
    file.indexOf('/') === -1 &&
    file.indexOf('\\') === -1;
}

function contentType(file) {
  var ext = path.extname(file);
  if (ext === '.html') {
    return 'text/html; charset=utf-8';
  }

  if (ext === '.css') {
    return 'text/css; charset=utf-8';
  }

  if (ext === '.js') {
    return 'application/javascript; charset=utf-8';
  }

  if (ext === '.json') {
    return 'application/json; charset=utf-8';
  }

  return 'text/plain; charset=utf-8';
}

module.exports = createStaticFiles;
