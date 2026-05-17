const cpsPanels = [
  {
    key: "js",
    title: "Raw JavaScript",
    subtitle: "what the user wrote",
    x: 32,
    y: 128,
    w: 282,
    h: 312,
    fill: "#082f3e",
    stroke: "#00b0f0",
    accent: "#00b0f0",
    lines: [
      "async function fetchData(value) {",
      "  try {",
      "    let result = await",
      "      Promise.resolve(value * 2);",
      "    let doubled = await",
      "      Promise.resolve(result + 10);",
      "    return doubled;",
      "  } catch (error) {",
      "    return 0;",
      "  }",
      "}"
    ]
  },
  {
    key: "sexpr",
    title: "S-expression IR",
    subtitle: "syntax becomes data",
    x: 339,
    y: 128,
    w: 282,
    h: 312,
    fill: "#2b1855",
    stroke: "#a78bfa",
    accent: "#a78bfa",
    lines: [
      "(async fetchData (value)",
      "  (block",
      "    (try",
      "      (block",
      "        (let result",
      "          (await",
      "            (Promise.resolve",
      "              (* value 2))))",
      "        (let doubled",
      "          (await",
      "            (Promise.resolve",
      "              (+ result 10))))",
      "        (return doubled))",
      "      (catch error",
      "        (return 0)))))"
    ]
  },
  {
    key: "cps",
    title: "CPS transformed",
    subtitle: "await becomes continuation flow",
    x: 646,
    y: 128,
    w: 282,
    h: 312,
    fill: "#064536",
    stroke: "#34d399",
    accent: "#34d399",
    lines: [
      "(function fetchData (value)",
      "  (return",
      "    (new Promise",
      "      (lambda (__resolve __reject)",
      "        (try",
      "          (then",
      "            (Promise.resolve",
      "              (* value 2))",
      "            (lambda (result)",
      "              (then",
      "                (Promise.resolve",
      "                  (+ result 10))",
      "                (lambda (doubled)",
      "                  (__resolve doubled)))))",
      "          (catch error",
      "            (__resolve 0)))))))"
    ]
  }
];

let cpsStage = 0;
let cpsPreviousStage = 0;
let cpsTransitionStarted = -1;
let cpsLastTime = 0;

function cpsClamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function cpsEase(value) {
  const t = cpsClamp(value, 0, 1);
  return t * t * (3 - 2 * t);
}

function cpsText(ctx, id, value, x, y, size, fill, opacity, weight, family) {
  const text = ctx.svg.layer.text(id, value, x, y, size, fill, opacity);
  text.set("font-family", family || "Arial Rounded MT Bold, Arial, Helvetica, sans-serif");
  text.set("font-weight", weight || "700");
  text.set("letter-spacing", "0");
  return text;
}

function cpsPanelOpacity(panelIndex, stage, progress) {
  const wasVisible = panelIndex <= cpsPreviousStage ? 1 : 0;
  const isVisible = panelIndex <= stage ? 1 : 0;
  return wasVisible + (isVisible - wasVisible) * progress;
}

function cpsPanelOffset(panelIndex, stage, progress) {
  if (panelIndex <= cpsPreviousStage) {
    return 0;
  }

  if (panelIndex <= stage) {
    return 38 * (1 - progress);
  }

  return 38;
}

function cpsSetPanel(ctx, panel, panelIndex, opacity, offset) {
  const groupOpacity = cpsClamp(opacity, 0, 1);
  const x = panel.x + offset;
  const dim = panelIndex === cpsStage ? 1 : 0.68;
  ctx.svg.id("cps-panel-" + panel.key).set("x", x.toFixed(2));
  ctx.svg.id("cps-panel-" + panel.key).set("opacity", (groupOpacity * dim).toFixed(3));
  ctx.svg.id("cps-panel-" + panel.key).set("stroke-width", panelIndex === cpsStage ? "2.6" : "1.4");
  ctx.svg.id("cps-title-" + panel.key).set("x", (x + 18).toFixed(2));
  ctx.svg.id("cps-title-" + panel.key).set("opacity", groupOpacity.toFixed(3));
  ctx.svg.id("cps-subtitle-" + panel.key).set("x", (x + 18).toFixed(2));
  ctx.svg.id("cps-subtitle-" + panel.key).set("opacity", (groupOpacity * 0.74).toFixed(3));

  for (let index = 0; index < panel.lines.length; index++) {
    const id = "cps-code-" + panel.key + "-" + index;
    ctx.svg.id(id).set("x", (x + 18).toFixed(2));
    ctx.svg.id(id).set("opacity", groupOpacity.toFixed(3));
  }
}

function cpsSetArrow(ctx, id, visible, pulse) {
  const opacity = visible ? 0.48 + pulse * 0.42 : 0;
  ctx.svg.id(id).set("opacity", opacity.toFixed(3));
  ctx.svg.id(id + "-head").set("opacity", opacity.toFixed(3));
}

function cpsApplyStage(ctx, time) {
  const progress = cpsTransitionStarted < 0
    ? 1
    : cpsEase((time - cpsTransitionStarted) / 420);

  for (let index = 0; index < cpsPanels.length; index++) {
    const panel = cpsPanels[index];
    cpsSetPanel(
      ctx,
      panel,
      index,
      cpsPanelOpacity(index, cpsStage, progress),
      cpsPanelOffset(index, cpsStage, progress));
  }

  const pulse = (Math.sin(time * 0.012) + 1) * 0.5;
  cpsSetArrow(ctx, "cps-arrow-js-sexpr", cpsStage >= 1, pulse);
  cpsSetArrow(ctx, "cps-arrow-sexpr-cps", cpsStage >= 2, pulse);

  ctx.svg.id("cps-stage-label").text((cpsStage + 1) + " / 3");
  ctx.svg.id("cps-stage-label").set("fill", cpsPanels[cpsStage].accent);

  if (progress >= 1) {
    cpsPreviousStage = cpsStage;
    cpsTransitionStarted = -1;
  }
}

function cpsGotoStage(ctx, nextStage) {
  const normalized = cpsClamp(nextStage, 0, cpsPanels.length - 1);
  if (normalized === cpsStage) {
    return;
  }

  cpsPreviousStage = cpsStage;
  cpsStage = normalized;
  cpsTransitionStarted = cpsLastTime;
  cpsApplyStage(ctx, cpsLastTime);
}

function cpsDrawPanel(ctx, panel) {
  const box = ctx.svg.layer.rect("cps-panel-" + panel.key, panel.x, panel.y, panel.w, panel.h, panel.fill, 0);
  box.set("stroke", panel.stroke);
  box.set("stroke-width", "1.4");

  cpsText(ctx, "cps-title-" + panel.key, panel.title, panel.x + 18, panel.y + 34, 18, "#f3f4f6", 0);
  cpsText(ctx, "cps-subtitle-" + panel.key, panel.subtitle, panel.x + 18, panel.y + 58, 10.5, panel.accent, 0);

  for (let index = 0; index < panel.lines.length; index++) {
    cpsText(
      ctx,
      "cps-code-" + panel.key + "-" + index,
      panel.lines[index],
      panel.x + 18,
      panel.y + 90 + index * 13.2,
      8.4,
      "#e5e7eb",
      0,
      "600",
      "Menlo, Consolas, monospace");
  }
}

slideScript("continuation-passing-style-translator.svg", {
  enter: function (ctx) {
    cpsStage = 0;
    cpsPreviousStage = 0;
    cpsTransitionStarted = -1;
    cpsLastTime = 0;

    ctx.svg.layer.rect("cps-generated-bg", 0, 0, 960, 540, "#0d0d0d", 1);
    cpsText(ctx, "cps-generated-title", "Continuation Passing Style", 36, 70, 40, "#00b0f0", 1);
    cpsText(ctx, "cps-generated-subtitle", "One async function, three representations of the same program.", 38, 104, 14.5, "#e5e7eb", 0.9);
    cpsText(ctx, "cps-stage-label", "1 / 3", 876, 72, 17, cpsPanels[0].accent, 1);

    ctx.svg.layer.path("cps-arrow-js-sexpr", "M 318 284 L 334 284", "none", "#f3f4f6", 2.8, 0);
    ctx.svg.layer.path("cps-arrow-js-sexpr-head", "M 334 284 L 324 278 L 324 290 Z", "#f3f4f6", "none", 0, 0);
    ctx.svg.layer.path("cps-arrow-sexpr-cps", "M 625 284 L 641 284", "none", "#f3f4f6", 2.8, 0);
    ctx.svg.layer.path("cps-arrow-sexpr-cps-head", "M 641 284 L 631 278 L 631 290 Z", "#f3f4f6", "none", 0, 0);

    for (let index = 0; index < cpsPanels.length; index++) {
      cpsDrawPanel(ctx, cpsPanels[index]);
    }

    cpsApplyStage(ctx, 0);
  },

  frame: function (ctx, time) {
    cpsLastTime = time;
    cpsApplyStage(ctx, time);
  },

  key: function (ctx, key) {
    if (key === "Space") {
      cpsGotoStage(ctx, (cpsStage + 1) % cpsPanels.length);
      return true;
    }

    if (key === "ArrowRight" && cpsStage < cpsPanels.length - 1) {
      cpsGotoStage(ctx, cpsStage + 1);
      return true;
    }

    if (key === "ArrowLeft" && cpsStage > 0) {
      cpsGotoStage(ctx, cpsStage - 1);
      return true;
    }

    if (key === "R") {
      cpsGotoStage(ctx, 0);
      return true;
    }

    return false;
  }
});
