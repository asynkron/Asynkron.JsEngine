var iter = {
  [Symbol.iterator]: function() {
    return {
      next: function() {
        return Object.defineProperty({}, "value", {
          get: function() { throw new Error("boom"); }
        });
      }
    };
  }
};

var res;
try {
  res = [0, ...iter];
  console.log('ok', res);
} catch (e) {
  console.log('caught', e && e.message);
}
