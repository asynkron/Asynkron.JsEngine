async function* asyncRange(start, end) {
    for (let i = start; i < end; i++) {
        yield i;
    }
}

let gen = asyncRange(0, 10);
let result = gen.next();
10;
