var fs = require('fs');

function createJsonDb(file) {
  function readData() {
    if (!fs.existsSync(file)) {
      return { issues: [] };
    }

    return JSON.parse(fs.readFileSync(file));
  }

  function writeData(data) {
    fs.writeFileSync(file, JSON.stringify(data, null, 2) + '\n');
  }

  function nextId(issues) {
    var max = 0;
    for (var i = 0; i < issues.length; i++) {
      if (issues[i].id > max) {
        max = issues[i].id;
      }
    }

    return max + 1;
  }

  function now() {
    return new Date().toISOString();
  }

  return {
    all: function () {
      return readData().issues;
    },

    get: function (id) {
      var numericId = Number(id);
      var issues = readData().issues;
      for (var i = 0; i < issues.length; i++) {
        if (issues[i].id === numericId) {
          return issues[i];
        }
      }

      return null;
    },

    create: function (input) {
      var data = readData();
      var created = {
        id: nextId(data.issues),
        title: input.title,
        owner: input.owner,
        status: input.status,
        priority: input.priority,
        createdAt: now(),
        updatedAt: now()
      };

      data.issues.push(created);
      writeData(data);
      return created;
    },

    update: function (id, patch) {
      var numericId = Number(id);
      var data = readData();
      for (var i = 0; i < data.issues.length; i++) {
        var issue = data.issues[i];
        if (issue.id !== numericId) {
          continue;
        }

        if (patch.title !== undefined) {
          issue.title = String(patch.title);
        }

        if (patch.owner !== undefined) {
          issue.owner = String(patch.owner);
        }

        if (patch.status !== undefined) {
          issue.status = String(patch.status);
        }

        if (patch.priority !== undefined) {
          issue.priority = String(patch.priority);
        }

        issue.updatedAt = now();
        writeData(data);
        return issue;
      }

      return null;
    }
  };
}

module.exports = createJsonDb;
