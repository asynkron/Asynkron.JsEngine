// Escalate string size to find where .NET Regex crashes
function buildString(args) {
  const loneCodePoints = args.loneCodePoints;
  const ranges = args.ranges;
  const CHUNK_SIZE = 10000;
  let result = String.fromCodePoint.apply(null, loneCodePoints);
  for (let i = 0; i < ranges.length; i++) {
    let range = ranges[i];
    let start = range[0];
    let end = range[1];
    let codePoints = [];
    for (let length = 0, codePoint = start; codePoint <= end; codePoint++) {
      codePoints[length++] = codePoint;
      if (length === CHUNK_SIZE) {
        result += String.fromCodePoint.apply(null, codePoints);
        codePoints.length = length = 0;
      }
    }
    result += String.fromCodePoint.apply(null, codePoints);
  }
  return result;
}

// Match the real Alphabetic test: ranges up to 0x323AF = ~200k code points
var s = buildString({
  loneCodePoints: [],
  ranges: [
    [0x0041, 0x005A],
    [0x0061, 0x007A],
    [0x00C0, 0x024F],
    [0x0400, 0x04FF],
    [0x0530, 0x058F],
    [0x0600, 0x06FF],
    [0x0900, 0x097F],
    [0x1000, 0x109F],
    [0x1100, 0x11FF],
    [0x1E00, 0x1FFF],
    [0x2C00, 0x2C5F],
    [0x3040, 0x30FF],
    [0x3400, 0x4DBF],
    [0x4E00, 0x9FFF],
    [0xAC00, 0xD7AF],
    [0x10000, 0x1007F],
    [0x10400, 0x1044F],
    [0x10800, 0x1083F],
    [0x1D400, 0x1D7FF],
    [0x20000, 0x2A6DF],
    [0x2A700, 0x2B73F],
    [0x2B740, 0x2B81F],
    [0x30000, 0x3134F]
  ]
});

var re = /^\p{Alphabetic}+$/u;
re.test(s);
"done";
