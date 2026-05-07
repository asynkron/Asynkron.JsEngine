var activeStatus = '';

function request(method, url, body) {
  return fetch(url, {
    method: method,
    headers: { 'Content-Type': 'application/json' },
    body: body ? JSON.stringify(body) : undefined
  }).then(function (res) {
    return res.json();
  });
}

function loadStatus() {
  request('GET', '/api/status').then(function (status) {
    document.getElementById('status').textContent =
      status.runtime + ' running ' + status.issueCount + ' issues';
  });
}

function loadIssues() {
  var url = '/api/issues';
  if (activeStatus) {
    url += '?status=' + encodeURIComponent(activeStatus);
  }

  request('GET', url).then(function (payload) {
    var host = document.getElementById('issues');
    host.innerHTML = '';

    for (var i = 0; i < payload.issues.length; i++) {
      host.appendChild(renderIssue(payload.issues[i]));
    }
  });
}

function renderIssue(issue) {
  var card = document.createElement('article');
  card.className = 'issue ' + issue.status;

  var title = document.createElement('h2');
  title.textContent = '#' + issue.id + ' ' + issue.title;
  card.appendChild(title);

  var meta = document.createElement('p');
  meta.textContent = issue.owner + ' · ' + issue.priority + ' · ' + issue.status;
  card.appendChild(meta);

  var toggle = document.createElement('button');
  toggle.textContent = issue.status === 'open' ? 'Close' : 'Reopen';
  toggle.onclick = function () {
    request('PATCH', '/api/issues/' + issue.id, {
      status: issue.status === 'open' ? 'closed' : 'open'
    }).then(function () {
      loadStatus();
      loadIssues();
    });
  };
  card.appendChild(toggle);

  return card;
}

document.getElementById('create').onclick = function () {
  request('POST', '/api/issues', {
    title: document.getElementById('title').value,
    owner: document.getElementById('owner').value || 'unassigned',
    priority: document.getElementById('priority').value,
    status: 'open'
  }).then(function () {
    document.getElementById('title').value = '';
    loadStatus();
    loadIssues();
  });
};

var buttons = document.querySelectorAll('[data-filter]');
for (var i = 0; i < buttons.length; i++) {
  buttons[i].onclick = function () {
    activeStatus = this.getAttribute('data-filter');
    loadIssues();
  };
}

loadStatus();
loadIssues();
