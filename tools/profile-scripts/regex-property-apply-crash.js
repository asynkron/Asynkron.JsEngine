// Test escalating .apply sizes to find where it breaks
var sizes = [1000, 5000, 10000, 50000, 100000];
for (var s = 0; s < sizes.length; s++) {
  var n = sizes[s];
  var arr = [];
  for (var i = 0; i < n; i++) {
    arr.push(65 + (i % 26));
  }
  try {
    var result = String.fromCodePoint.apply(null, arr);
    // print is available in ProfileRunner
  } catch(e) {
    // will show as error
    throw new Error("apply failed at size " + n + ": " + e.message);
  }
}
"done";
