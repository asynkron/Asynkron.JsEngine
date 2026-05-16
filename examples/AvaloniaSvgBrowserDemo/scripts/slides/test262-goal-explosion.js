function goalVectorNoise(seed) {
  const value = Math.sin(seed * 12.9898) * 43758.5453;
  return value - Math.floor(value);
}

function goalVectorPoint(angle, radius) {
  return {
    x: 480 + Math.cos(angle) * radius,
    y: 270 + Math.sin(angle) * radius * 0.72
  };
}

function goalVectorPath(a, b) {
  return "M " + a.x.toFixed(2) + " " + a.y.toFixed(2) + " L " + b.x.toFixed(2) + " " + b.y.toFixed(2);
}

function goalVectorArrowHead(id, from, to, color, ctx) {
  const angle = Math.atan2(to.y - from.y, to.x - from.x);
  const size = 6.8;
  const left = {
    x: to.x - Math.cos(angle - 0.48) * size,
    y: to.y - Math.sin(angle - 0.48) * size
  };
  const right = {
    x: to.x - Math.cos(angle + 0.48) * size,
    y: to.y - Math.sin(angle + 0.48) * size
  };
  const d = "M " + to.x.toFixed(2) + " " + to.y.toFixed(2) +
    " L " + left.x.toFixed(2) + " " + left.y.toFixed(2) +
    " L " + right.x.toFixed(2) + " " + right.y.toFixed(2) + " Z";
  ctx.svg.layer.path(id, d, color, "none", 0, 0.62);
}

const goalTestColors = {
  gray: "#9ca3af",
  green: "#22c55e",
  red: "#ef4444"
};

const goalTestCount = 720;
let goalTestNodes = [];

function goalTestInitialStatus(index) {
  return goalVectorNoise(index + 211) < 0.14 ? "green" : "red";
}

function goalTestIsStubborn(index) {
  return goalVectorNoise(index + 419) < 0.045;
}

function goalTestStatus(test, elapsed) {
  if (elapsed < test.revealAt) {
    return "gray";
  }

  if (test.initialStatus === "green" || elapsed >= test.resolveAt) {
    return "green";
  }

  return "red";
}

function goalTestApplyStatus(test, status, ctx) {
  if (test.status === status) {
    return;
  }

  const color = goalTestColors[status];
  test.status = status;
  ctx.svg.id(test.nodeId).set("fill", color);
  ctx.svg.id(test.nodeId).set("opacity", status === "gray" ? "0.44" : "0.78");

  if (test.forceId.length > 0) {
    ctx.svg.id(test.forceId).set("stroke", color);
    ctx.svg.id(test.forceId).set("opacity", status === "gray" ? "0.24" : "0.52");
    ctx.svg.id(test.forceHeadId).set("fill", color);
    ctx.svg.id(test.forceHeadId).set("opacity", status === "gray" ? "0.34" : "0.76");
  }
}

slideScript("test262-goal-explosion.svg", {
  enter: function (ctx) {
    const center = { x: 480, y: 270 };
    goalTestNodes = [];

    ctx.svg.layer.rect("goal-generated-bg", 0, 0, 960, 540, "#02060c", 1);

    for (let index = 0; index < goalTestCount; index++) {
      const angle = index * 2.399963 + goalVectorNoise(index) * 0.42;
      const band = index % 12;
      const radius = 92 + band * 34 + goalVectorNoise(index + 37) * 58;
      const point = goalVectorPoint(angle, radius);
      const size = 1.8 + goalVectorNoise(index + 11) * 5.2;
      const deltaX = center.x - point.x;
      const deltaY = center.y - point.y;
      const distance = Math.sqrt(deltaX * deltaX + deltaY * deltaY);
      const towardGoal = Math.atan2(deltaY, deltaX);
      const availableLength = Math.max(12, distance - 86);
      const length = Math.min(availableLength, 24 + goalVectorNoise(index + 73) * 74);
      const lineStart = {
        x: point.x,
        y: point.y
      };
      const lineEnd = {
        x: point.x + Math.cos(towardGoal) * length,
        y: point.y + Math.sin(towardGoal) * length
      };
      const nodeId = "goal-node-" + index;
      const forceId = distance > 92 ? "goal-force-" + index : "";
      const forceHeadId = distance > 92 ? "goal-force-head-" + index : "";

      if (distance > 92) {
        ctx.svg.layer.path(forceId, goalVectorPath(lineStart, lineEnd), "none", goalTestColors.gray, 1.1, 0.24);
        goalVectorArrowHead(forceHeadId, lineStart, lineEnd, goalTestColors.gray, ctx);
      }

      ctx.svg.layer.circle(nodeId, point.x, point.y, size, goalTestColors.gray, 0.44);
      goalTestNodes[index] = {
        nodeId: nodeId,
        forceId: forceId,
        forceHeadId: forceHeadId,
        revealAt: 350 + goalVectorNoise(index + 701) * 3600,
        resolveAt: goalTestIsStubborn(index)
          ? 60000
          : 4400 + goalVectorNoise(index + 809) * 9000,
        initialStatus: goalTestInitialStatus(index),
        status: "gray"
      };
    }

    ctx.svg.layer.circle("goal-core-halo", center.x, center.y, 98, "#064e3b", 0.48);
    ctx.svg.layer.circle("goal-core", center.x, center.y, 64, "#052f22", 0.97);
    ctx.svg.layer.text("goal-core-label", "goal", 421, 290, 50, "#8df06f", 0.98);
  },

  frame: function (ctx, time, elapsed) {
    for (let index = 0; index < goalTestNodes.length; index++) {
      const test = goalTestNodes[index];
      goalTestApplyStatus(test, goalTestStatus(test, elapsed), ctx);
    }
  }
});
