const astWalkSteps = [
  { node: "1", text: "1. Enter the if special form.", results: [] },
  { node: "2", text: "2. Evaluate the condition first.", results: [] },
  { node: "3", text: "3. Call the gt operator.", results: [] },
  { node: "4", text: "4. Literal evaluates immediately.", results: ["ast-result-4"] },
  { node: "3", text: "Back to 3 with left value 10.", results: ["ast-result-4"] },
  { node: "5", text: "5. Resolve the x symbol.", results: ["ast-result-4"] },
  { node: "6", text: "6. Lookup returns 5.", results: ["ast-result-4", "ast-result-6"] },
  { node: "3", text: "Back to 3: 10 > 5 -> true.", results: ["ast-result-4", "ast-result-6", "ast-result-3"] },
  { node: "2", text: "Return true back to 2.", results: ["ast-result-3", "ast-result-2"] },
  { node: "1", text: "Return true back to 1.", results: ["ast-result-3", "ast-result-2", "ast-result-1"] },
  { node: "7a", text: "7a. Select the then branch.", results: ["ast-result-1"] },
  { node: "8a", text: "8a. Execute thenExpr.", results: ["ast-result-1", "ast-result-8a"] }
];

const astWalkNodes = {
  "1": { x: 436, y: 115, w: 158, h: 42, label: "1. if special form", fill: "#0b2a3b", stroke: "#00b0f0" },
  "2": { x: 263, y: 200, w: 126, h: 42, label: "2. condition", fill: "#2b1855", stroke: "#a78bfa" },
  "7a": { x: 441, y: 200, w: 148, h: 42, label: "7a. then branch", fill: "#2b1855", stroke: "#a78bfa" },
  "7b": { x: 632, y: 200, w: 148, h: 42, label: "7b. else branch", fill: "#2b1855", stroke: "#a78bfa" },
  "3": { x: 244, y: 284, w: 164, h: 43, label: "3. gt operator call", fill: "#0b3a31", stroke: "#34d399" },
  "8a": { x: 450, y: 284, w: 130, h: 43, label: "8a. thenExpr", fill: "#0b3a31", stroke: "#34d399" },
  "8b": { x: 642, y: 284, w: 128, h: 43, label: "8b. elseExpr", fill: "#0b3a31", stroke: "#34d399" },
  "4": { x: 181, y: 369, w: 124, h: 43, label: "4. 10 literal", fill: "#3a2017", stroke: "#ffc000" },
  "5": { x: 347, y: 369, w: 122, h: 43, label: "5. x symbol", fill: "#3a2017", stroke: "#ffc000" },
  "6": { x: 300, y: 453, w: 217, h: 42, label: "6. lookup x in environment", fill: "#4b1530", stroke: "#fb7185" }
};

const astWalkEdges = [
  { id: "1-2", path: "M 436 157 C 356 176 326 176 326 200", arrowX: 326, arrowY: 200 },
  { id: "1-7a", path: "M 515 157 L 515 200", arrowX: 515, arrowY: 200 },
  { id: "1-7b", path: "M 594 157 C 685 177 706 177 706 200", arrowX: 706, arrowY: 200 },
  { id: "2-3", path: "M 326 242 L 326 284", arrowX: 326, arrowY: 284 },
  { id: "7a-8a", path: "M 515 242 L 515 284", arrowX: 515, arrowY: 284 },
  { id: "7b-8b", path: "M 706 242 L 706 284", arrowX: 706, arrowY: 284 },
  { id: "3-4", path: "M 244 327 C 204 346 191 346 191 369", arrowX: 191, arrowY: 369 },
  { id: "3-5", path: "M 367 327 C 401 345 408 345 408 369", arrowX: 408, arrowY: 369 },
  { id: "5-6", path: "M 408 412 L 408 453", arrowX: 408, arrowY: 453 }
];

let astWalkCurrentStep = -1;

function astWalkText(ctx, id, value, x, y, size, fill, opacity) {
  const text = ctx.svg.layer.text(id, value, x, y, size, fill, opacity);
  text.set("font-family", "Arial, Helvetica, sans-serif");
  text.set("font-weight", "700");
  text.set("letter-spacing", "0");
  return text;
}

function astWalkNode(ctx, key) {
  const node = astWalkNodes[key];
  const box = ctx.svg.layer.rect("ast-node-" + key, node.x, node.y, node.w, node.h, node.fill, 1);
  box.set("stroke", node.stroke);
  box.set("stroke-width", "1.7");

  astWalkText(
    ctx,
    "ast-node-title-" + key,
    node.label,
    node.x + 24,
    node.y + node.h / 2 + 6,
    14,
    "#f3f4f6",
    1);

  const active = ctx.svg.layer.rect(
    "ast-active-" + key,
    node.x - 5,
    node.y - 5,
    node.w + 10,
    node.h + 10,
    "none",
    0);
  active.set("stroke", "#ffffff");
  active.set("stroke-width", "4");
}

function astWalkDrawEdge(ctx, edge) {
  ctx.svg.layer.path("ast-edge-" + edge.id, edge.path, "none", "#d8d8d8", 1.15, 1);
  ctx.svg.layer.path(
    "ast-edge-arrow-" + edge.id,
    "M " + (edge.arrowX - 5) + " " + (edge.arrowY - 9) +
      " L " + (edge.arrowX + 5) + " " + (edge.arrowY - 9) +
      " L " + edge.arrowX + " " + edge.arrowY + " Z",
    "#d8d8d8",
    "none",
    0,
    1);
}

function astWalkHighlight(ctx, key, state) {
  const node = astWalkNodes[key];
  ctx.svg.id("ast-node-" + key).set("fill", node.fill);
  ctx.svg.id("ast-node-" + key).set("stroke", node.stroke);
  ctx.svg.id("ast-node-" + key).set("stroke-width", state === "active" ? "2.5" : "1.7");
  ctx.svg.id("ast-node-title-" + key).set("fill", "#f3f4f6");
  ctx.svg.id("ast-active-" + key).set("opacity", state === "active" ? "1" : "0");
}

function astWalkResult(ctx, id, value, x, y, width) {
  const box = ctx.svg.layer.rect(id + "-box", x, y, width, 24, "#0d0d0d", 0);
  box.set("stroke", "#ffc000");
  box.set("stroke-width", "1.5");
  astWalkText(ctx, id, value, x + 8, y + 18, 11.5, "#ffc000", 0);
}

function astWalkSetResultOpacity(ctx, id, opacity) {
  ctx.svg.id(id + "-box").set("opacity", opacity);
  ctx.svg.id(id).set("opacity", opacity);
}

function astWalkApplyStep(ctx, stepIndex) {
  if (stepIndex === astWalkCurrentStep) {
    return;
  }

  astWalkCurrentStep = stepIndex;
  const step = astWalkSteps[stepIndex];
  const visited = {};
  for (let index = 0; index <= stepIndex; index++) {
    visited[astWalkSteps[index].node] = true;
  }

  for (const key in astWalkNodes) {
    astWalkHighlight(ctx, key, visited[key] ? "done" : "idle");
  }
  astWalkHighlight(ctx, step.node, "active");

  ctx.svg.id("ast-step-counter").text((stepIndex + 1) + " / " + astWalkSteps.length);
  ctx.svg.id("ast-step-text").text(step.text);

  const allResults = ["ast-result-4", "ast-result-6", "ast-result-3", "ast-result-2", "ast-result-1", "ast-result-8a"];
  for (let index = 0; index < allResults.length; index++) {
    astWalkSetResultOpacity(ctx, allResults[index], 0);
  }
  for (let index = 0; index < step.results.length; index++) {
    astWalkSetResultOpacity(ctx, step.results[index], 1);
  }
}

slideScript("ast-walking-evaluation.svg", {
  enter: function (ctx) {
    astWalkCurrentStep = -1;

    ctx.svg.layer.rect("ast-generated-bg", 0, 0, 960, 540, "#0d0d0d", 1);
    astWalkText(ctx, "ast-generated-title", "AST Walking", 24, 54, 56, "#00b0f0", 1);

    for (let index = 0; index < astWalkEdges.length; index++) {
      astWalkDrawEdge(ctx, astWalkEdges[index]);
    }

    for (const key in astWalkNodes) {
      astWalkNode(ctx, key);
    }

    astWalkResult(ctx, "ast-result-4", "return 10", 198, 420, 74);
    astWalkResult(ctx, "ast-result-6", "return 5", 526, 462, 68);
    astWalkResult(ctx, "ast-result-3", "10 > 5 -> true", 414, 322, 112);
    astWalkResult(ctx, "ast-result-2", "return true", 400, 218, 84);
    astWalkResult(ctx, "ast-result-1", "return true", 604, 128, 84);
    astWalkResult(ctx, "ast-result-8a", "return true", 548, 330, 84);

    const stepPanel = ctx.svg.layer.rect("ast-step-panel", 26, 120, 218, 74, "#0d0d0d", 0.92);
    stepPanel.set("stroke", "#00b0f0");
    stepPanel.set("stroke-width", "1.4");
    astWalkText(ctx, "ast-step-counter", "1 / " + astWalkSteps.length, 42, 148, 13, "#ffc000", 1);
    astWalkText(ctx, "ast-step-text", "", 42, 177, 12.5, "#f3f4f6", 1);

    astWalkApplyStep(ctx, 0);
  },

  key: function (ctx, key) {
    if (key === "Space") {
      const next = (astWalkCurrentStep + 1) % astWalkSteps.length;
      astWalkApplyStep(ctx, next);
      return true;
    }

    if (key === "R") {
      astWalkApplyStep(ctx, 0);
      return true;
    }

    return false;
  }
});
