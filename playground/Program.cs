using System;
using Asynkron.JsEngine;
using Asynkron.JsEngine.JsTypes;

internal static class Program
{
    private static async Task Main()
    {
        await using var engine = new JsEngine();
        try
        {
            var result = await engine.Evaluate(@"
// Copyright (C) 2014 the V8 project authors. All rights reserved.
// This code is governed by the BSD license found in the LICENSE file.
/*---
es6id: 14.5
description: >
    class this access restriction 2
---*/
class Base {
  constructor(a, b) {
    var o = new Object();
    o.prp = a + b;
    return o;
  }
}

class Subclass extends Base {
  constructor(a, b) {
    var exn;
    try {
      this.prp1 = 3;
    } catch (e) {
      exn = e;
    }
    console.log('exn type', exn && exn.name);
    super(a, b);
    console.log('after super this:', this);
    console.log('this.prp', this.prp, 'this.prp1', this.prp1);
    return this;
  }
}

var b = new Base(1, 2);
console.log('b.prp', b.prp);

var s = new Subclass(2, -1);
console.log('constructed s', s);
console.log('s.prp', s.prp, 's.prp1', s.prp1, 'hasOwn', s.hasOwnProperty('prp1'));

class Subclass2 extends Base {
  constructor(x) {
    super(1,2);

    if (x < 0) return;

    var called = false;
    function tmp() { called = true; return 3; }
    var exn = null;
    try {
      super(tmp(),4);
    } catch (e) { exn = e; }
    console.log('second super exn', exn && exn.name);
    console.log('called?', called);
  }
}

var s2 = new Subclass2(1);
console.log('s2.prp', s2.prp);

var s3 = new Subclass2(-1);
console.log('s3.prp', s3.prp);

class BadSubclass extends Base {
  constructor() {}
}

try { new BadSubclass(); } catch (e) { console.log('bad subclass error', e && e.name); }
'done';
");

            Console.WriteLine($"Test completed! Result: {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test FAILED: {ex.Message}");
            Console.WriteLine($"Stack: {ex.StackTrace}");
        }
    }
}
