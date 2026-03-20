// Isolate: just buildString with a huge range - no regex
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

// This is the nonMatchSymbols range from the Connector_Punctuation test
var s = buildString({
  loneCodePoints: [],
  ranges: [
    [0x00DC00, 0x00DFFF],
    [0x000000, 0x00005E],
    [0x000060, 0x00203E],
    [0x002041, 0x002053],
    [0x002055, 0x00DBFF],
    [0x00E000, 0x00FE32],
    [0x00FE35, 0x00FE4C],
    [0x00FE50, 0x00FF3E],
    [0x00FF40, 0x10FFFF]
  ]
});
s.length;
