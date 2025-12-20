let result = "";
for (let i = 0; i < 20000; i++) {
    result += "x";
}
let upper = result.toUpperCase();
let split = result.split("");
let joined = split.join("-");
joined.length;
