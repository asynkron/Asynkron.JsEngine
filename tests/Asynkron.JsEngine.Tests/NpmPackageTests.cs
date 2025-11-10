using Asynkron.JsEngine;

namespace Asynkron.JsEngine.Tests;

/// <summary>
/// Tests validating the JavaScript engine using real-world npm package implementations.
/// These packages are selected because they:
/// - Are pure JavaScript (no Node.js or browser APIs)
/// - Focus on computation and string/array utilities
/// - Are well-known and battle-tested
/// - Help validate the engine's JavaScript compatibility
/// </summary>
public class NpmPackageTests
{
    // ========================================
    // left-pad: String padding utility
    // ========================================

    [Fact]
    public async Task LeftPad_PadsStringWithSpaces()
    {
        var engine = new JsEngine();
        
        // Implementation based on the famous left-pad npm package
        var script = @"
            function leftPad(str, len, ch) {
                str = str + '';
                let i = -1;
                if (!ch && ch !== 0) ch = ' ';
                len = len - str.length;
                while (++i < len) {
                    str = ch + str;
                }
                return str;
            }
            
            leftPad('foo', 5, ' ');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("  foo", result);
    }

    [Fact]
    public async Task LeftPad_PadsStringWithCustomCharacter()
    {
        var engine = new JsEngine();
        
        var script = @"
            function leftPad(str, len, ch) {
                str = str + '';
                let i = -1;
                if (!ch && ch !== 0) ch = ' ';
                len = len - str.length;
                while (++i < len) {
                    str = ch + str;
                }
                return str;
            }
            
            leftPad('42', 5, '0');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("00042", result);
    }

    [Fact]
    public async Task LeftPad_HandlesEmptyString()
    {
        var engine = new JsEngine();
        
        var script = @"
            function leftPad(str, len, ch) {
                str = str + '';
                let i = -1;
                if (!ch && ch !== 0) ch = ' ';
                len = len - str.length;
                while (++i < len) {
                    str = ch + str;
                }
                return str;
            }
            
            leftPad('', 3, 'a');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("aaa", result);
    }

    [Fact]
    public async Task LeftPad_NoModificationIfAlreadyLongEnough()
    {
        var engine = new JsEngine();
        
        var script = @"
            function leftPad(str, len, ch) {
                str = str + '';
                let i = -1;
                if (!ch && ch !== 0) ch = ' ';
                len = len - str.length;
                while (++i < len) {
                    str = ch + str;
                }
                return str;
            }
            
            leftPad('hello', 3, '0');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("hello", result);
    }

    // ========================================
    // is-odd / is-even: Number parity checking
    // ========================================

    [Fact]
    public async Task IsOdd_IdentifiesOddNumbers()
    {
        var engine = new JsEngine();
        
        var script = @"
            function isOdd(num) {
                let i = Math.abs(num);
                return !!(i & 1);
            }
            
            let result1 = isOdd(1);
            let result2 = isOdd(3);
            let result3 = isOdd(2);
            let result4 = isOdd(0);
            
            result1 && result2 && !result3 && !result4;
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task IsEven_IdentifiesEvenNumbers()
    {
        var engine = new JsEngine();
        
        var script = @"
            function isEven(num) {
                let i = Math.abs(num);
                return !(i & 1);
            }
            
            let result1 = isEven(0);
            let result2 = isEven(2);
            let result3 = isEven(4);
            let result4 = isEven(1);
            let result5 = isEven(3);
            
            result1 && result2 && result3 && !result4 && !result5;
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task IsOdd_HandlesNegativeNumbers()
    {
        var engine = new JsEngine();
        
        var script = @"
            function isOdd(num) {
                let i = Math.abs(num);
                return !!(i & 1);
            }
            
            let result1 = isOdd(-1);
            let result2 = isOdd(-3);
            let result3 = isOdd(-2);
            
            result1 && result2 && !result3;
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.True((bool)result!);
    }

    // ========================================
    // clamp: Constrain number to range
    // ========================================

    [Fact]
    public async Task Clamp_ReturnsNumberWithinRange()
    {
        var engine = new JsEngine();
        
        var script = @"
            function clamp(value, min, max) {
                return Math.min(Math.max(value, min), max);
            }
            
            clamp(5, 0, 10);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal(5d, result);
    }

    [Fact]
    public async Task Clamp_ClampsToMinimum()
    {
        var engine = new JsEngine();
        
        var script = @"
            function clamp(value, min, max) {
                return Math.min(Math.max(value, min), max);
            }
            
            clamp(-5, 0, 10);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal(0d, result);
    }

    [Fact]
    public async Task Clamp_ClampsToMaximum()
    {
        var engine = new JsEngine();
        
        var script = @"
            function clamp(value, min, max) {
                return Math.min(Math.max(value, min), max);
            }
            
            clamp(15, 0, 10);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal(10d, result);
    }

    // ========================================
    // in-range: Check if number is in range
    // ========================================

    [Fact]
    public async Task InRange_ReturnsTrueForNumberInRange()
    {
        var engine = new JsEngine();
        
        var script = @"
            function inRange(value, start, end) {
                if (end === undefined) {
                    end = start;
                    start = 0;
                }
                return value >= start && value < end;
            }
            
            inRange(5, 0, 10);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task InRange_ReturnsFalseForNumberOutOfRange()
    {
        var engine = new JsEngine();
        
        var script = @"
            function inRange(value, start, end) {
                if (end === undefined) {
                    end = start;
                    start = 0;
                }
                return value >= start && value < end;
            }
            
            inRange(15, 0, 10);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.False((bool)result!);
    }

    [Fact]
    public async Task InRange_HandlesSingleArgumentForm()
    {
        var engine = new JsEngine();
        
        var script = @"
            function inRange(value, start, end) {
                if (end === undefined) {
                    end = start;
                    start = 0;
                }
                return value >= start && value < end;
            }
            
            inRange(5, 10, undefined);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.True((bool)result!);
    }

    // ========================================
    // fibonacci: Generate fibonacci sequence
    // ========================================

    [Fact]
    public async Task Fibonacci_GeneratesCorrectSequence()
    {
        var engine = new JsEngine();
        
        var script = @"
            function fibonacci(n) {
                if (n <= 1) return n;
                return fibonacci(n - 1) + fibonacci(n - 2);
            }
            
            let result = [];
            for (let i = 0; i < 10; i = i + 1) {
                result.push(fibonacci(i));
            }
            
            result.join(',');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("0,1,1,2,3,5,8,13,21,34", result);
    }

    [Fact]
    public async Task Fibonacci_IterativeVersion()
    {
        var engine = new JsEngine();
        
        var script = @"
            function fibonacciIterative(n) {
                if (n <= 1) return n;
                
                let a = 0;
                let b = 1;
                
                for (let i = 2; i <= n; i = i + 1) {
                    let temp = a + b;
                    a = b;
                    b = temp;
                }
                
                return b;
            }
            
            fibonacciIterative(10);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal(55d, result);
    }

    // ========================================
    // Luhn algorithm: Credit card validation
    // ========================================

    [Fact]
    public async Task Luhn_ValidatesValidCreditCard()
    {
        var engine = new JsEngine();
        
        var script = @"
            function luhn(cardNumber) {
                let str = cardNumber + '';
                let sum = 0;
                let isEven = false;
                
                for (let i = str.length - 1; i >= 0; i = i - 1) {
                    let digit = str.charCodeAt(i) - 48;
                    
                    if (isEven) {
                        digit = digit * 2;
                        if (digit > 9) {
                            digit = digit - 9;
                        }
                    }
                    
                    sum = sum + digit;
                    isEven = !isEven;
                }
                
                return sum % 10 === 0;
            }
            
            luhn('4532015112830366');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task Luhn_RejectsInvalidCreditCard()
    {
        var engine = new JsEngine();
        
        var script = @"
            function luhn(cardNumber) {
                let str = cardNumber + '';
                let sum = 0;
                let isEven = false;
                
                for (let i = str.length - 1; i >= 0; i = i - 1) {
                    let digit = str.charCodeAt(i) - 48;
                    
                    if (isEven) {
                        digit = digit * 2;
                        if (digit > 9) {
                            digit = digit - 9;
                        }
                    }
                    
                    sum = sum + digit;
                    isEven = !isEven;
                }
                
                return sum % 10 === 0;
            }
            
            luhn('1234567812345678');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.False((bool)result!);
    }

    // ========================================
    // array-shuffle: Fisher-Yates shuffle
    // ========================================

    [Fact]
    public async Task ArrayShuffle_MaintainsAllElements()
    {
        var engine = new JsEngine();
        
        var script = @"
            function shuffle(array) {
                let currentIndex = array.length;
                let randomIndex = 0;
                
                while (currentIndex !== 0) {
                    // Use a deterministic 'random' for testing
                    randomIndex = Math.floor(Math.abs(Math.sin(currentIndex) * 10000) * currentIndex);
                    randomIndex = randomIndex % currentIndex;
                    currentIndex = currentIndex - 1;
                    
                    let temp = array[currentIndex];
                    array[currentIndex] = array[randomIndex];
                    array[randomIndex] = temp;
                }
                
                return array;
            }
            
            let arr = [1, 2, 3, 4, 5];
            let shuffled = shuffle(arr);
            
            // Verify all elements are still present
            let sorted = shuffled.sort(function(a, b) { return a - b; });
            sorted.join(',');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("1,2,3,4,5", result);
    }

    // ========================================
    // deepEqual: Deep object/array comparison
    // ========================================

    [Fact]
    public async Task DeepEqual_ComparesSimpleObjects()
    {
        var engine = new JsEngine();
        
        var script = @"
            function deepEqual(a, b) {
                if (a === b) return true;
                
                if (a === null || b === null) return false;
                if (a === undefined || b === undefined) return false;
                
                if (typeof a !== typeof b) return false;
                
                if (typeof a !== 'object') return false;
                
                let keysA = Object.keys(a);
                let keysB = Object.keys(b);
                
                if (keysA.length !== keysB.length) return false;
                
                for (let i = 0; i < keysA.length; i = i + 1) {
                    let key = keysA[i];
                    if (!deepEqual(a[key], b[key])) return false;
                }
                
                return true;
            }
            
            let obj1 = { a: 1, b: 2, c: { d: 3 } };
            let obj2 = { a: 1, b: 2, c: { d: 3 } };
            
            deepEqual(obj1, obj2);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.True((bool)result!);
    }

    [Fact]
    public async Task DeepEqual_DetectsDifferences()
    {
        var engine = new JsEngine();
        
        var script = @"
            function deepEqual(a, b) {
                if (a === b) return true;
                
                if (a === null || b === null) return false;
                if (a === undefined || b === undefined) return false;
                
                if (typeof a !== typeof b) return false;
                
                if (typeof a !== 'object') return false;
                
                let keysA = Object.keys(a);
                let keysB = Object.keys(b);
                
                if (keysA.length !== keysB.length) return false;
                
                for (let i = 0; i < keysA.length; i = i + 1) {
                    let key = keysA[i];
                    if (!deepEqual(a[key], b[key])) return false;
                }
                
                return true;
            }
            
            let obj1 = { a: 1, b: 2, c: { d: 3 } };
            let obj2 = { a: 1, b: 2, c: { d: 4 } };
            
            deepEqual(obj1, obj2);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.False((bool)result!);
    }

    // ========================================
    // camelCase: Convert string to camelCase
    // ========================================

    [Fact]
    public async Task CamelCase_ConvertsHyphenatedString()
    {
        var engine = new JsEngine();
        
        var script = @"
            function camelCase(str) {
                let result = '';
                let capitalizeNext = false;
                
                for (let i = 0; i < str.length; i = i + 1) {
                    let char = str.charAt(i);
                    
                    if (char === '-' || char === '_' || char === ' ') {
                        capitalizeNext = true;
                    } else {
                        if (capitalizeNext) {
                            result = result + char.toUpperCase();
                            capitalizeNext = false;
                        } else {
                            result = result + char.toLowerCase();
                        }
                    }
                }
                
                return result;
            }
            
            camelCase('hello-world-test');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("helloWorldTest", result);
    }

    [Fact]
    public async Task CamelCase_HandlesUnderscores()
    {
        var engine = new JsEngine();
        
        var script = @"
            function camelCase(str) {
                let result = '';
                let capitalizeNext = false;
                
                for (let i = 0; i < str.length; i = i + 1) {
                    let char = str.charAt(i);
                    
                    if (char === '-' || char === '_' || char === ' ') {
                        capitalizeNext = true;
                    } else {
                        if (capitalizeNext) {
                            result = result + char.toUpperCase();
                            capitalizeNext = false;
                        } else {
                            result = result + char.toLowerCase();
                        }
                    }
                }
                
                return result;
            }
            
            camelCase('foo_bar_baz');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("fooBarBaz", result);
    }

    // ========================================
    // kebabCase: Convert string to kebab-case
    // ========================================

    [Fact]
    public async Task KebabCase_ConvertsCamelCase()
    {
        var engine = new JsEngine();
        
        var script = @"
            function kebabCase(str) {
                let result = '';
                
                for (let i = 0; i < str.length; i = i + 1) {
                    let char = str.charAt(i);
                    let code = str.charCodeAt(i);
                    
                    if (code >= 65 && code <= 90) {
                        if (i > 0) {
                            result = result + '-';
                        }
                        result = result + char.toLowerCase();
                    } else if (char === '_' || char === ' ') {
                        result = result + '-';
                    } else {
                        result = result + char;
                    }
                }
                
                return result;
            }
            
            kebabCase('helloWorldTest');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("hello-world-test", result);
    }

    // ========================================
    // capitalize: Capitalize first letter
    // ========================================

    [Fact]
    public async Task Capitalize_CapitalizesFirstLetter()
    {
        var engine = new JsEngine();
        
        var script = @"
            function capitalize(str) {
                if (str.length === 0) return str;
                return str.charAt(0).toUpperCase() + str.slice(1).toLowerCase();
            }
            
            capitalize('hello world');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("Hello world", result);
    }

    [Fact]
    public async Task Capitalize_HandlesEmptyString()
    {
        var engine = new JsEngine();
        
        var script = @"
            function capitalize(str) {
                if (str.length === 0) return str;
                return str.charAt(0).toUpperCase() + str.slice(1).toLowerCase();
            }
            
            capitalize('');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("", result);
    }

    // ========================================
    // flatten: Flatten nested arrays
    // ========================================

    [Fact]
    public async Task Flatten_FlattensNestedArrays()
    {
        var engine = new JsEngine();
        
        var script = @"
            function flatten(arr) {
                let result = [];
                
                for (let i = 0; i < arr.length; i = i + 1) {
                    let item = arr[i];
                    if (typeof item === 'object' && item.length !== undefined) {
                        for (let j = 0; j < item.length; j = j + 1) {
                            result.push(item[j]);
                        }
                    } else {
                        result.push(item);
                    }
                }
                
                return result;
            }
            
            let nested = [1, [2, 3], 4, [5, 6]];
            let flat = flatten(nested);
            flat.join(',');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("1,2,3,4,5,6", result);
    }

    // ========================================
    // sum: Sum array of numbers
    // ========================================

    [Fact]
    public async Task Sum_AddsArrayOfNumbers()
    {
        var engine = new JsEngine();
        
        var script = @"
            function sum(arr) {
                let total = 0;
                for (let i = 0; i < arr.length; i = i + 1) {
                    total = total + arr[i];
                }
                return total;
            }
            
            sum([1, 2, 3, 4, 5]);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal(15d, result);
    }

    [Fact]
    public async Task Sum_HandlesEmptyArray()
    {
        var engine = new JsEngine();
        
        var script = @"
            function sum(arr) {
                let total = 0;
                for (let i = 0; i < arr.length; i = i + 1) {
                    total = total + arr[i];
                }
                return total;
            }
            
            sum([]);
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal(0d, result);
    }

    // ========================================
    // unique: Remove duplicates from array
    // ========================================

    [Fact]
    public async Task Unique_RemovesDuplicates()
    {
        var engine = new JsEngine();
        
        var script = @"
            function unique(arr) {
                let result = [];
                
                for (let i = 0; i < arr.length; i = i + 1) {
                    let found = false;
                    
                    for (let j = 0; j < result.length; j = j + 1) {
                        if (result[j] === arr[i]) {
                            found = true;
                        }
                    }
                    
                    if (!found) {
                        result.push(arr[i]);
                    }
                }
                
                return result;
            }
            
            let arr = [1, 2, 2, 3, 3, 3, 4, 5, 5];
            let uniq = unique(arr);
            uniq.join(',');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("1,2,3,4,5", result);
    }

    // ========================================
    // chunk: Split array into chunks
    // ========================================

    [Fact]
    public async Task Chunk_SplitsArrayIntoChunks()
    {
        var engine = new JsEngine();
        
        var script = @"
            function chunk(arr, size) {
                let result = [];
                
                for (let i = 0; i < arr.length; i = i + size) {
                    let chunk = [];
                    
                    for (let j = 0; j < size && i + j < arr.length; j = j + 1) {
                        chunk.push(arr[i + j]);
                    }
                    
                    result.push(chunk);
                }
                
                return result;
            }
            
            let arr = [1, 2, 3, 4, 5, 6, 7, 8];
            let chunks = chunk(arr, 3);
            chunks.length;
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal(3d, result);
    }

    [Fact]
    public async Task Chunk_VerifyChunkContents()
    {
        var engine = new JsEngine();
        
        var script = @"
            function chunk(arr, size) {
                let result = [];
                
                for (let i = 0; i < arr.length; i = i + size) {
                    let chunk = [];
                    
                    for (let j = 0; j < size && i + j < arr.length; j = j + 1) {
                        chunk.push(arr[i + j]);
                    }
                    
                    result.push(chunk);
                }
                
                return result;
            }
            
            let arr = [1, 2, 3, 4, 5];
            let chunks = chunk(arr, 2);
            chunks[0].join(',') + ';' + chunks[1].join(',') + ';' + chunks[2].join(',');
        ";
        
        var result = engine.EvaluateSync(script);
        Assert.Equal("1,2;3,4;5", result);
    }
}
