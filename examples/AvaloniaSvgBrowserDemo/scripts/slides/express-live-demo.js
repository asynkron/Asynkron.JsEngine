const expressDemoButtons = [
  { id: "start", label: "Start upstream app", x: 56, y: 124, w: 180, h: 42, fill: "#0b3a31", stroke: "#34d399" },
  { id: "curl", label: "curl /", x: 252, y: 124, w: 158, h: 42, fill: "#3a2017", stroke: "#ffc000" },
  { id: "stop", label: "Stop host", x: 426, y: 124, w: 116, h: 42, fill: "#4b1530", stroke: "#fb7185" }
];

let expressDemoLastHost = "";
let expressDemoLastCurl = "";
let expressDemoLastStatus = "";

function expressDemoText(ctx, id, value, x, y, size, fill, opacity) {
  const text = ctx.svg.layer.text(id, value, x, y, size, fill, opacity);
  text.set("font-family", "Menlo, Consolas, monospace");
  return text;
}

function expressDemoLabel(ctx, id, value, x, y, size, fill) {
  const text = ctx.svg.layer.text(id, value, x, y, size, fill, 1);
  text.set("font-family", "Arial, Helvetica, sans-serif");
  text.set("font-weight", "700");
  text.set("text-anchor", "middle");
  return text;
}

function expressDemoContains(button, x, y) {
  return x >= button.x && x <= button.x + button.w && y >= button.y && y <= button.y + button.h;
}

function expressDemoLines(value, maxLines, maxChars) {
  const normalized = String(value || "").replace(/\r/g, "");
  const rawLines = normalized.length === 0 ? ["waiting..."] : normalized.split("\n");
  const result = [];
  const start = Math.max(0, rawLines.length - maxLines);
  for (let index = start; index < rawLines.length; index++) {
    let line = rawLines[index];
    while (line.length > maxChars) {
      result.push(line.slice(0, maxChars));
      line = "  " + line.slice(maxChars);
    }

    result.push(line);
  }

  return result.slice(Math.max(0, result.length - maxLines));
}

function expressDemoRenderLines(ctx, prefix, value, x, y, maxLines, maxChars) {
  const lines = expressDemoLines(value, maxLines, maxChars);
  for (let index = 0; index < maxLines; index++) {
    const id = prefix + "-" + index;
    const line = index < lines.length ? lines[index] : "";
    ctx.svg.id(id).text(line);
  }
}

function expressDemoSetStatus(ctx) {
  const status = demo.expressStatus();
  if (status === expressDemoLastStatus) {
    return;
  }

  expressDemoLastStatus = status;
  if (status === "ready") {
    ctx.svg.id("express-demo-state-dot").set("fill", "#22c55e");
    ctx.svg.id("express-demo-state").text("upstream app ready on localhost:3000");
    return;
  }

  if (status === "starting") {
    ctx.svg.id("express-demo-state-dot").set("fill", "#ffc000");
    ctx.svg.id("express-demo-state").text("starting upstream app... waiting for localhost:3000");
    return;
  }

  ctx.svg.id("express-demo-state-dot").set("fill", "#ef4444");
  ctx.svg.id("express-demo-state").text("host stopped");
}

slideScript("express-live-demo.svg", {
  enter: function (ctx) {
    expressDemoLastHost = "";
    expressDemoLastCurl = "";
    expressDemoLastStatus = "";

    for (let index = 0; index < expressDemoButtons.length; index++) {
      const button = expressDemoButtons[index];
      ctx.svg.layer.rect("express-demo-button-" + button.id, button.x, button.y, button.w, button.h, button.fill, 1);
      ctx.svg.id("express-demo-button-" + button.id).set("stroke", button.stroke);
      ctx.svg.id("express-demo-button-" + button.id).set("stroke-width", "2");
      expressDemoLabel(ctx, "express-demo-button-label-" + button.id, button.label, button.x + button.w / 2, button.y + 27, 14, "#f8fafc");
    }

    ctx.svg.layer.circle("express-demo-state-dot", 574, 145, 7, "#ef4444", 1);
    expressDemoText(ctx, "express-demo-state", "host stopped", 590, 150, 13, "#cbd5e1", 1);

    ctx.svg.layer.rect("express-demo-host-bg", 56, 190, 404, 286, "#101827", 1);
    ctx.svg.id("express-demo-host-bg").set("stroke", "#00b0f0");
    ctx.svg.id("express-demo-host-bg").set("stroke-width", "1.5");
    ctx.svg.layer.rect("express-demo-curl-bg", 500, 190, 404, 286, "#101827", 1);
    ctx.svg.id("express-demo-curl-bg").set("stroke", "#ffc000");
    ctx.svg.id("express-demo-curl-bg").set("stroke-width", "1.5");

    expressDemoLabel(ctx, "express-demo-host-title", "terminal 1: JsEngine host", 258, 220, 18, "#00b0f0");
    expressDemoLabel(ctx, "express-demo-curl-title", "terminal 2: curl", 702, 220, 18, "#ffc000");

    for (let index = 0; index < 13; index++) {
      expressDemoText(ctx, "express-demo-host-line-" + index, "", 76, 252 + index * 16, 11, "#d1d5db", 1);
      expressDemoText(ctx, "express-demo-curl-line-" + index, "", 520, 252 + index * 16, 11, "#d1d5db", 1);
    }

    expressDemoRenderLines(ctx, "express-demo-host-line", "$ click Start upstream app", 76, 252, 13, 50);
    expressDemoRenderLines(ctx, "express-demo-curl-line", "$ click curl / after host is ready", 520, 252, 13, 50);
    expressDemoSetStatus(ctx);
  },

  frame: function (ctx) {
    const hostOutput = demo.hostOutput();
    if (hostOutput !== expressDemoLastHost) {
      expressDemoLastHost = hostOutput;
      expressDemoRenderLines(ctx, "express-demo-host-line", hostOutput, 76, 252, 13, 50);
    }

    const curlOutput = demo.curlOutput();
    if (curlOutput !== expressDemoLastCurl) {
      expressDemoLastCurl = curlOutput;
      expressDemoRenderLines(ctx, "express-demo-curl-line", curlOutput, 520, 252, 13, 50);
    }

    expressDemoSetStatus(ctx);
  },

  click: function (ctx, x, y) {
    for (let index = 0; index < expressDemoButtons.length; index++) {
      const button = expressDemoButtons[index];
      if (!expressDemoContains(button, x, y)) {
        continue;
      }

      if (button.id === "start") {
        demo.startExpress();
      } else if (button.id === "curl") {
        demo.curl("/");
      } else if (button.id === "stop") {
        demo.stopExpress();
      }

      return true;
    }

    return false;
  },

  key: function (ctx, key) {
    if (key === "Space") {
      demo.startExpress();
      return true;
    }

    if (key === "R") {
      demo.curl("/");
      return true;
    }

    return false;
  },

  leave: function () {
    demo.stopExpress();
  }
});
