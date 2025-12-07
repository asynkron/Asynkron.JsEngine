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
var templateObject;

function tag(parameter) {
  templateObject = parameter;
}

tag`hello${1}world`;

console.log('templateObject:', templateObject);
console.log('Array.isArray(templateObject):', Array.isArray(templateObject));
console.log('templateObject.raw:', templateObject.raw);
console.log('Array.isArray(templateObject.raw):', Array.isArray(templateObject.raw));
console.log('templateObject[0]:', templateObject[0]);
console.log('templateObject[1]:', templateObject[1]);
console.log('templateObject.length:', templateObject.length);
console.log('typeof templateObject:', typeof templateObject);
console.log('templateObject instanceof Array:', templateObject instanceof Array);

// Check 'raw' property descriptor
var rawDesc = Object.getOwnPropertyDescriptor(templateObject, 'raw');
console.log('rawDesc:', rawDesc);
if (rawDesc) {
  console.log('  writable:', rawDesc.writable);
  console.log('  enumerable:', rawDesc.enumerable);
  console.log('  configurable:', rawDesc.configurable);
  console.log('  value:', rawDesc.value);
}

// Check '0' property descriptor
var zeroDesc = Object.getOwnPropertyDescriptor(templateObject, '0');
console.log('zeroDesc for 0:', zeroDesc);
if (zeroDesc) {
  console.log('  writable:', zeroDesc.writable);
  console.log('  enumerable:', zeroDesc.enumerable);
  console.log('  configurable:', zeroDesc.configurable);
  console.log('  value:', zeroDesc.value);
}

// Check 'length' property descriptor
var lengthDesc = Object.getOwnPropertyDescriptor(templateObject, 'length');
console.log('lengthDesc:', lengthDesc);
if (lengthDesc) {
  console.log('  writable:', lengthDesc.writable);
  console.log('  enumerable:', lengthDesc.enumerable);
  console.log('  configurable:', lengthDesc.configurable);
  console.log('  value:', lengthDesc.value);
}

// Check raw's property descriptors
var rawZeroDesc = Object.getOwnPropertyDescriptor(templateObject.raw, '0');
console.log('rawZeroDesc:', rawZeroDesc);
if (rawZeroDesc) {
  console.log('  writable:', rawZeroDesc.writable);
  console.log('  enumerable:', rawZeroDesc.enumerable);
  console.log('  configurable:', rawZeroDesc.configurable);
}

var rawLengthDesc = Object.getOwnPropertyDescriptor(templateObject.raw, 'length');
console.log('rawLengthDesc:', rawLengthDesc);
if (rawLengthDesc) {
  console.log('  writable:', rawLengthDesc.writable);
  console.log('  enumerable:', rawLengthDesc.enumerable);
  console.log('  configurable:', rawLengthDesc.configurable);
}

console.log('Object.prototype.hasOwnProperty.call(templateObject, raw):', Object.prototype.hasOwnProperty.call(templateObject, 'raw'));
console.log('Object.isFrozen(templateObject):', Object.isFrozen(templateObject));
console.log('Object.isFrozen(templateObject.raw):', Object.isFrozen(templateObject.raw));
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
