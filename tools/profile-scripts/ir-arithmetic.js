var total = 0;
for (var i = 0; i < 200000; i++) {
    total = total + ((i * 3 + 7) % 11);
}
total;
