const ecmaConfetti = [
  { id: "ecma-confetti-l-00", x: 339, y: 166, dx: 170, dy: 28, color: "#ffc000", size: 4, delay: 0 },
  { id: "ecma-confetti-l-01", x: 339, y: 166, dx: 142, dy: -24, color: "#00b0f0", size: 3, delay: 70 },
  { id: "ecma-confetti-l-02", x: 339, y: 166, dx: 198, dy: 72, color: "#ff4f81", size: 3, delay: 140 },
  { id: "ecma-confetti-l-03", x: 339, y: 166, dx: 126, dy: 54, color: "#ffffff", size: 2, delay: 210 },
  { id: "ecma-confetti-l-04", x: 339, y: 166, dx: 220, dy: -8, color: "#34d399", size: 3, delay: 280 },
  { id: "ecma-confetti-l-05", x: 339, y: 166, dx: 162, dy: 106, color: "#a78bfa", size: 3, delay: 350 },
  { id: "ecma-confetti-l-06", x: 339, y: 166, dx: 246, dy: 36, color: "#fb7185", size: 2, delay: 420 },
  { id: "ecma-confetti-l-07", x: 339, y: 166, dx: 116, dy: -46, color: "#fbbf24", size: 4, delay: 490 },
  { id: "ecma-confetti-l-08", x: 339, y: 166, dx: 206, dy: 118, color: "#22d3ee", size: 3, delay: 560 },
  { id: "ecma-confetti-l-09", x: 339, y: 166, dx: 184, dy: -62, color: "#f472b6", size: 2, delay: 630 },
  { id: "ecma-confetti-r-00", x: 620, y: 166, dx: -170, dy: 28, color: "#ffc000", size: 4, delay: 35 },
  { id: "ecma-confetti-r-01", x: 620, y: 166, dx: -142, dy: -24, color: "#00b0f0", size: 3, delay: 105 },
  { id: "ecma-confetti-r-02", x: 620, y: 166, dx: -198, dy: 72, color: "#ff4f81", size: 3, delay: 175 },
  { id: "ecma-confetti-r-03", x: 620, y: 166, dx: -126, dy: 54, color: "#ffffff", size: 2, delay: 245 },
  { id: "ecma-confetti-r-04", x: 620, y: 166, dx: -220, dy: -8, color: "#34d399", size: 3, delay: 315 },
  { id: "ecma-confetti-r-05", x: 620, y: 166, dx: -162, dy: 106, color: "#a78bfa", size: 3, delay: 385 },
  { id: "ecma-confetti-r-06", x: 620, y: 166, dx: -246, dy: 36, color: "#fb7185", size: 2, delay: 455 },
  { id: "ecma-confetti-r-07", x: 620, y: 166, dx: -116, dy: -46, color: "#fbbf24", size: 4, delay: 525 },
  { id: "ecma-confetti-r-08", x: 620, y: 166, dx: -206, dy: 118, color: "#22d3ee", size: 3, delay: 595 },
  { id: "ecma-confetti-r-09", x: 620, y: 166, dx: -184, dy: -62, color: "#f472b6", size: 2, delay: 665 }
];

function ecmaEaseOut(value) {
  return 1 - Math.pow(1 - value, 3);
}

function ecmaGlowText(ctx, id, value, x, y, size, fill, opacity) {
  const text = ctx.svg.layer.text(id, value, x, y, size, fill, opacity);
  text.set("font-family", "Arial, Helvetica, sans-serif");
  text.set("font-weight", "700");
  text.set("letter-spacing", "0");
  return text;
}

slideScript("ecma-262-94000-unit-tests.svg", {
  enter: function (ctx) {
    ecmaGlowText(ctx, "ecma-unit-glow-outer", "94 000 unit tests", 262, 326, 56, "#ffc000", 0);
    ecmaGlowText(ctx, "ecma-unit-glow-mid", "94 000 unit tests", 263, 325, 54, "#ffd966", 0);
    ecmaGlowText(ctx, "ecma-unit-glow-core", "94 000 unit tests", 264, 324, 52, "#fff2a8", 0);

    for (let index = 0; index < ecmaConfetti.length; index++) {
      const piece = ecmaConfetti[index];
      ctx.svg.layer.circle(piece.id, piece.x, piece.y, piece.size, piece.color, 0);
    }
  },

  frame: function (ctx, time, elapsed) {
    const pulse = (Math.sin(elapsed * 0.005) + 1) / 2;
    const glow = 0.38 + pulse * 0.42;
    ctx.svg.id("ecma-unit-glow-outer").set("opacity", (glow * 0.20).toFixed(3));
    ctx.svg.id("ecma-unit-glow-outer").set("font-size", (59 + pulse * 5).toFixed(2));
    ctx.svg.id("ecma-unit-glow-outer").set("x", (258 - pulse * 4).toFixed(2));
    ctx.svg.id("ecma-unit-glow-outer").set("y", (327 + pulse * 2).toFixed(2));

    ctx.svg.id("ecma-unit-glow-mid").set("opacity", (glow * 0.24).toFixed(3));
    ctx.svg.id("ecma-unit-glow-mid").set("font-size", (56 + pulse * 3).toFixed(2));
    ctx.svg.id("ecma-unit-glow-mid").set("x", (260 - pulse * 2).toFixed(2));
    ctx.svg.id("ecma-unit-glow-mid").set("y", (326 + pulse).toFixed(2));

    ctx.svg.id("ecma-unit-glow-core").set("opacity", (0.08 + pulse * 0.08).toFixed(3));
    ctx.svg.id("ecma-unit-glow-core").set("font-size", (53 + pulse * 1.4).toFixed(2));

    for (let index = 0; index < ecmaConfetti.length; index++) {
      const piece = ecmaConfetti[index];
      const local = (elapsed - piece.delay) % 1500;

      if (local < 0) {
        ctx.svg.id(piece.id).set("opacity", "0");
        continue;
      }

      const progress = local / 1500;
      const eased = ecmaEaseOut(progress);
      const wobble = Math.sin(progress * Math.PI * 5 + index) * 10;
      const x = piece.x + piece.dx * eased;
      const y = piece.y + piece.dy * eased + 74 * progress * progress + wobble;
      const opacity = Math.max(0, 1 - progress * 1.2);
      const radius = Math.max(1.2, piece.size * (1 - progress * 0.42));

      ctx.svg.id(piece.id).set("cx", x.toFixed(2));
      ctx.svg.id(piece.id).set("cy", y.toFixed(2));
      ctx.svg.id(piece.id).set("r", radius.toFixed(2));
      ctx.svg.id(piece.id).set("opacity", opacity.toFixed(3));
    }
  }
});
