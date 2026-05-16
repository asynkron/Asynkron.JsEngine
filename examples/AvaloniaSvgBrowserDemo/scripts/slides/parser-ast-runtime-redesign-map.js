const slide16Words = [];
const slide16Circle = {
  cx: 478,
  cy: 314,
  rx: 318,
  ry: 56
};

function slide16AddWords(lineId, words, x, y, size, color) {
  let cursor = x;
  for (let index = 0; index < words.length; index++) {
    const word = words[index];
    const id = lineId + "-word-" + index;
    slide16Words.push({ id: id, word: word, x: cursor, y: y, size: size, color: color });
    cursor += word.length * size * 0.55 + size * 0.45;
  }
}

function slide16Text(ctx, id, value, x, y, size, fill, opacity) {
  const text = ctx.svg.layer.text(id, value, x, y, size, fill, opacity);
  text.set("font-family", "Arial, Helvetica, sans-serif");
  text.set("font-weight", "700");
  return text;
}

function slide16Clamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function slide16EaseOutBack(value) {
  const clamped = slide16Clamp(value, 0, 1);
  const c1 = 1.70158;
  const c3 = c1 + 1;
  return 1 + c3 * Math.pow(clamped - 1, 3) + c1 * Math.pow(clamped - 1, 2);
}

function slide16PartialOval(progress) {
  const steps = Math.max(2, Math.floor(96 * slide16Clamp(progress, 0, 1)));
  let path = "";
  for (let index = 0; index <= steps; index++) {
    const angle = -Math.PI + (Math.PI * 2 * index) / 96;
    const x = slide16Circle.cx + Math.cos(angle) * slide16Circle.rx;
    const y = slide16Circle.cy + Math.sin(angle) * slide16Circle.ry;
    path += (index === 0 ? "M " : " L ") + x.toFixed(2) + " " + y.toFixed(2);
  }

  return path;
}

function slide16Patch(ctx, id, x, y, width, height) {
  ctx.svg.layer.rect(id, x, y, width, height, "#0d0d0d", 0.94);
}

slide16AddWords("slide16-improved", ["Improved", "Lexer/Parser"], 362, 44, 17, "#f3f4f6");
slide16AddWords("slide16-memory", ["Memory", "models"], 177, 110, 17, "#f3f4f6");
slide16AddWords("slide16-sexpr", ["S-Expr", "to", "Typed", "AST"], 490, 100, 17, "#f3f4f6");
slide16AddWords("slide16-generators", ["Redesign", "Generators"], 328, 162, 17, "#f3f4f6");
slide16AddWords("slide16-async", ["Redesign", "Async", "Await"], 168, 236, 16, "#f3f4f6");
slide16AddWords("slide16-stdlib", ["The", "first", "steps", "towards", "a", "StdLib"], 407, 247, 16, "#f3f4f6");
slide16AddWords("slide16-runtime", ["Runtime", "logs"], 203, 308, 16, "#f3f4f6");
slide16AddWords("slide16-distributed", ["Distributed"], 407, 293, 15.5, "#f3f4f6");
slide16AddWords("slide16-tracing", ["Tracing", "inside", "the", "runtime"], 500, 293, 15.5, "#f3f4f6");
slide16AddWords("slide16-spy", ["Runtime", "spy", "-", "capture", "variables", "at", "given", "points"], 363, 351, 15.5, "#f3f4f6");
slide16AddWords("slide16-tests", ["~1500", "unit", "tests,", "including", "some", "known", "benchmark", "source", "files"], 182, 402, 16, "#f3f4f6");

slideScript("parser-ast-runtime-redesign-map.svg", {
  enter: function (ctx) {
    slide16Patch(ctx, "slide16-cover-top", 165, 32, 660, 180);
    slide16Patch(ctx, "slide16-cover-mid", 150, 220, 680, 155);
    slide16Patch(ctx, "slide16-cover-bottom", 172, 388, 650, 36);

    for (let index = 0; index < slide16Words.length; index++) {
      const item = slide16Words[index];
      slide16Text(ctx, item.id, item.word, item.x, item.y, item.size, item.color, 0);
    }

    ctx.svg.layer.path("slide16-distributed-strike", "M 407 291 L 493 291", "none", "#f3f4f6", 1.8, 0);
    ctx.svg.layer.path("slide16-circle", "M 160 314", "none", "#ffffff", 1.7, 0);
  },

  frame: function (ctx, time, elapsed) {
    for (let index = 0; index < slide16Words.length; index++) {
      const item = slide16Words[index];
      const progress = slide16Clamp((elapsed - index * 115) / 260, 0, 1);
      const pop = slide16EaseOutBack(progress);
      const size = item.size * (0.7 + pop * 0.3);
      ctx.svg.id(item.id).set("opacity", progress.toFixed(3));
      ctx.svg.id(item.id).set("font-size", size.toFixed(2));
    }

    const strikeProgress = slide16Clamp((elapsed - 36 * 115) / 240, 0, 1);
    ctx.svg.id("slide16-distributed-strike").set("opacity", strikeProgress.toFixed(3));

    const circleProgress = slide16Clamp((elapsed - slide16Words.length * 115 - 220) / 900, 0, 1);
    ctx.svg.id("slide16-circle").set("d", slide16PartialOval(circleProgress));
    ctx.svg.id("slide16-circle").set("opacity", circleProgress.toFixed(3));
  }
});
