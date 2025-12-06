// Test for template literal line terminator normalization
// According to ES spec 11.8.6.1, TV should normalize:
// <CR><LF> → \n
// <CR> → \n
// <LF> → \n

var result = `
\r\n\r`;

console.log("Result:", JSON.stringify(result));
console.log("Expected:", JSON.stringify("\n\n\n"));
console.log("Match:", result === "\n\n\n");
