// Test Array.prototype.with
try {
    const result = [0, 1, 2].with(-3, 7);
    console.log("result (-3):", JSON.stringify(result));
} catch(e) {
    console.log("error (-3):", typeof e, e?.constructor?.name, e?.message);
}

try {
    const result = [0, 1, 2].with(-4, 7);
    console.log("result (-4):", JSON.stringify(result));
} catch(e) {
    console.log("error (-4):", typeof e, e?.constructor?.name, e?.message);
}

try {
    const result = [0, 1, 2].with(-Infinity, 7);
    console.log("result (-Infinity):", JSON.stringify(result));
} catch(e) {
    console.log("error (-Infinity):", typeof e, e?.constructor?.name, e?.message);
}
