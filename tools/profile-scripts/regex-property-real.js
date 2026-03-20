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

var re = /^\P{ASCII_Hex_Digit}+$/u;

// Binary search for the failing range endpoint
var r1 = re.test(buildString({ loneCodePoints: [], ranges: [[0x67, 0x1000]] }));
var r2 = re.test(buildString({ loneCodePoints: [], ranges: [[0x67, 0x5000]] }));
var r3 = re.test(buildString({ loneCodePoints: [], ranges: [[0x67, 0x9000]] }));
var r4 = re.test(buildString({ loneCodePoints: [], ranges: [[0x67, 0xD000]] }));
var r5 = re.test(buildString({ loneCodePoints: [], ranges: [[0x67, 0xD7FF]] }));
var r6 = re.test(buildString({ loneCodePoints: [], ranges: [[0x67, 0xD800]] }));
var r7 = re.test(buildString({ loneCodePoints: [], ranges: [[0x67, 0xD900]] }));

throw new Error("RESULTS: 0x1000=" + r1 + " 0x5000=" + r2 + " 0x9000=" + r3 + " 0xD000=" + r4 + " 0xD7FF=" + r5 + " 0xD800=" + r6 + " 0xD900=" + r7);
