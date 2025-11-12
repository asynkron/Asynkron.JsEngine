// The Great Computer Language Shootout
//  http://shootout.alioth.debian.org
//
//  Contributed by Ian Osgood
//
// TEST STATUS: FAILING
// Error: Expected -1286749544853 but got 0
// Root Cause: Bitwise sieve algorithm produces wrong result - likely issues with:
//   - Bit manipulation: isPrime[i>>5] & 1<<(i&31)
//   - Bitwise AND/OR operations on array elements
//   - Signed integer handling (result should be large negative number)
// The algorithm relies heavily on bit-level operations which aren't working correctly.

function pad(n,width) {
  var s = n.toString();
  while (s.length < width) s = ' ' + s;
  return s;
}

function primes(isPrime, n) {
  var i, count = 0, m = 10000<<n, size = m+31>>5;
  __debug(); // Debug: function entry with parameters

  for (i=0; i<size; i++) isPrime[i] = 0xffffffff;

  for (i=2; i<m; i++)
    if (isPrime[i>>5] & 1<<(i&31)) {
      for (var j=i+i; j<m; j+=i)
        isPrime[j>>5] &= ~(1<<(j&31));
      count++;
    }
  __debug(); // Debug: after main loop, check count
}

function sieve() {
    __debug(); // Debug: sieve function entry
    for (var i = 4; i <= 4; i++) {
        var isPrime = new Array((10000<<i)+31>>5);
        primes(isPrime, i);
        __debug(); // Debug: after primes call
    }
    return isPrime;
}

var result = sieve();
__debug(); // Debug: after sieve, check result

var sum = 0;
for (var i = 0; i < result.length; ++i)
    sum += result[i];

__debug(); // Debug: after sum calculation
var expected = -1286749544853;
if (sum != expected)
    throw "ERROR: bad result: expected " + expected + " but got " + sum;

