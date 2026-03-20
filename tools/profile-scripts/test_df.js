var df = new Intl.DurationFormat('en');
var opts = df.resolvedOptions();
var keys = Object.keys(opts);
var result = '';
for (var i = 0; i < keys.length; i++) {
  result += keys[i] + '=' + opts[keys[i]] + '\n';
}
result;
