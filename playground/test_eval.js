// Test eval++ in non-strict mode
try {
    var result = eval++;
    console.log("Result:", result);
    console.log("Type:", typeof result);
} catch (e) {
    console.log("Error:", e.toString());
}
