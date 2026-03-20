// Construct a regex with property escape and get its .NET pattern via toString
var re = /\p{General_Category=Connector_Punctuation}/u;
// The test: match a single character - if this is slow, the pattern itself is bad
var start = Date.now();
for (var i = 0; i < 10000; i++) {
  re.test("_");
}
var elapsed = Date.now() - start;
elapsed;
