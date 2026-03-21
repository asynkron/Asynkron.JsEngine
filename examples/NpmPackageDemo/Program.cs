using Asynkron.JsEngine;

Console.WriteLine("=== Asynkron.JsEngine - NPM Package Compatibility Demo ===");
Console.WriteLine();
Console.WriteLine("This demo showcases various npm package implementations running in the JS engine.");
Console.WriteLine();

var engine = new JsEngine();

// ============================================
// Demo 1: left-pad - String padding utility
// ============================================
Console.WriteLine("1. left-pad - String padding utility");
Console.WriteLine("-------------------------------------");

var leftPadScript = """

                        function leftPad(str, len, ch) {
                            str = str + '';
                            if (!ch && ch !== 0) ch = ' ';
                            len = len - str.length;
                            for (let i = 0; i < len; i = i + 1) {
                                str = ch + str;
                            }
                            return str;
                        }
                        
                        leftPad('Hello', 10, ' ');

                    """;

var padded = engine.Evaluate(leftPadScript).Result;
Console.WriteLine($"leftPad('Hello', 10, ' ') = '{padded}'");

var paddedNumber = engine.Evaluate("leftPad('42', 6, '0');").Result;
Console.WriteLine($"leftPad('42', 6, '0') = '{paddedNumber}'");
Console.WriteLine();

// ============================================
// Demo 2: is-odd / is-even - Parity checking
// ============================================
Console.WriteLine("2. is-odd / is-even - Parity checking");
Console.WriteLine("--------------------------------------");

var parityScript = """

                       function isOdd(num) {
                           let i = Math.abs(num);
                           return !!(i & 1);
                       }
                       
                       function isEven(num) {
                           let i = Math.abs(num);
                           return !(i & 1);
                       }
                       
                       let numbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
                       let odds = [];
                       let evens = [];
                       
                       for (let i = 0; i < numbers.length; i = i + 1) {
                           if (isOdd(numbers[i])) {
                               odds.push(numbers[i]);
                           } else {
                               evens.push(numbers[i]);
                           }
                       }
                       
                       'Odds: ' + odds.join(', ') + ' | Evens: ' + evens.join(', ');

                   """;

var result = engine.Evaluate(parityScript).Result;
Console.WriteLine(result);
Console.WriteLine();

// ============================================
// Demo 3: clamp - Number range constraint
// ============================================
Console.WriteLine("3. clamp - Number range constraint");
Console.WriteLine("-----------------------------------");

var clampScript = """

                      function clamp(value, min, max) {
                          return Math.min(Math.max(value, min), max);
                      }
                      
                      let values = [-10, 0, 5, 15, 25];
                      let clamped = [];
                      
                      for (let i = 0; i < values.length; i = i + 1) {
                          clamped.push(clamp(values[i], 0, 10));
                      }
                      
                      'Values: [' + values.join(', ') + '] => Clamped [0-10]: [' + clamped.join(', ') + ']';

                  """;

var clamped = engine.Evaluate(clampScript).Result;
Console.WriteLine(clamped);
Console.WriteLine();

// ============================================
// Demo 4: camelCase - String formatting
// ============================================
Console.WriteLine("4. camelCase - String formatting");
Console.WriteLine("---------------------------------");

var camelCaseScript = """

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
                          
                          let strings = ['hello-world', 'foo_bar_baz', 'test case string'];
                          let converted = [];
                          
                          for (let i = 0; i < strings.length; i = i + 1) {
                              converted.push(camelCase(strings[i]));
                          }
                          
                          converted.join(', ');

                      """;

var camelCased = engine.Evaluate(camelCaseScript).Result;
Console.WriteLine($"Converted to camelCase: {camelCased}");
Console.WriteLine();

// ============================================
// Demo 5: fibonacci - Sequence generation
// ============================================
Console.WriteLine("5. fibonacci - Sequence generation");
Console.WriteLine("-----------------------------------");

var fibonacciScript = """

                          function fibonacci(n) {
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
                          
                          let sequence = [];
                          for (let i = 0; i < 15; i = i + 1) {
                              sequence.push(fibonacci(i));
                          }
                          
                          'First 15 Fibonacci numbers: ' + sequence.join(', ');

                      """;

var fibonacci = engine.Evaluate(fibonacciScript).Result;
Console.WriteLine(fibonacci);
Console.WriteLine();

// ============================================
// Demo 6: luhn - Credit card validation
// ============================================
Console.WriteLine("6. luhn - Credit card validation");
Console.WriteLine("---------------------------------");

var luhnScript = """

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
                     
                     let cards = [
                         '4532015112830366',
                         '6011514433546201',
                         '1234567812345678',
                         '5425233430109903'
                     ];
                     
                     let results = [];
                     for (let i = 0; i < cards.length; i = i + 1) {
                         let card = cards[i];
                         let valid = luhn(card);
                         let masked = card.substring(0, 4) + '********' + card.substring(12);
                         results.push(masked + ': ' + (valid ? 'VALID' : 'INVALID'));
                     }
                     
                     results.join(' | ');

                 """;

var validation = engine.Evaluate(luhnScript).Result;
Console.WriteLine(validation);
Console.WriteLine();

// ============================================
// Demo 7: chunk - Array chunking
// ============================================
Console.WriteLine("7. chunk - Array chunking");
Console.WriteLine("--------------------------");

var chunkScript = """

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
                      
                      let data = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
                      let chunks = chunk(data, 3);
                      
                      let formatted = [];
                      for (let i = 0; i < chunks.length; i = i + 1) {
                          formatted.push('[' + chunks[i].join(', ') + ']');
                      }
                      
                      'Data: [' + data.join(', ') + '] => Chunks of 3: ' + formatted.join(', ');

                  """;

var chunked = engine.Evaluate(chunkScript).Result;
Console.WriteLine(chunked);
Console.WriteLine();

// ============================================
// Demo 8: unique - Duplicate removal
// ============================================
Console.WriteLine("8. unique - Duplicate removal");
Console.WriteLine("------------------------------");

var uniqueScript = """

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
                       
                       let duplicates = [1, 2, 2, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 5];
                       let uniques = unique(duplicates);
                       
                       'Input: [' + duplicates.join(', ') + '] => Unique: [' + uniques.join(', ') + ']';

                   """;

var unique = engine.Evaluate(uniqueScript).Result;
Console.WriteLine(unique);
Console.WriteLine();

// ============================================
// Demo 9: deepEqual - Object comparison
// ============================================
Console.WriteLine("9. deepEqual - Object comparison");
Console.WriteLine("---------------------------------");

var deepEqualScript = """

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
                          
                          let obj1 = { name: 'Alice', age: 30, address: { city: 'NYC', zip: '10001' } };
                          let obj2 = { name: 'Alice', age: 30, address: { city: 'NYC', zip: '10001' } };
                          let obj3 = { name: 'Bob', age: 30, address: { city: 'NYC', zip: '10001' } };
                          
                          let result1 = deepEqual(obj1, obj2) ? 'EQUAL' : 'NOT EQUAL';
                          let result2 = deepEqual(obj1, obj3) ? 'EQUAL' : 'NOT EQUAL';
                          
                          'obj1 vs obj2: ' + result1 + ' | obj1 vs obj3: ' + result2;

                      """;

var comparison = engine.Evaluate(deepEqualScript).Result;
Console.WriteLine(comparison);
Console.WriteLine();

Console.WriteLine("=== All demos completed successfully! ===");
Console.WriteLine();
Console.WriteLine("These examples demonstrate that Asynkron.JsEngine can successfully run");
Console.WriteLine("pure JavaScript npm packages without Node.js or browser dependencies.");
Console.WriteLine();
Console.WriteLine("For more information, see: docs/NPM_PACKAGE_COMPATIBILITY.md");
