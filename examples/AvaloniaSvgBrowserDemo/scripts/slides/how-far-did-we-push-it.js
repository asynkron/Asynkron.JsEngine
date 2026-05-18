const pushStarCount = 150;
const pushStars = [];
const pushPalette = ["#ffffff", "#00f0ff", "#ffc000", "#ff4f81", "#a78bfa", "#34d399"];
let pushStarfieldActive = false;

function pushNoise(seed) {
  const value = Math.sin(seed * 127.1 + 311.7) * 43758.5453;
  return value - Math.floor(value);
}

function pushClamp(value, min, max) {
  return Math.max(min, Math.min(max, value));
}

function pushBuildStars() {
  if (pushStars.length > 0) {
    return;
  }

  for (let index = 0; index < pushStarCount; index++) {
    const angle = pushNoise(index + 11) * Math.PI * 2;
    const radius = Math.pow(pushNoise(index + 23), 0.58) * 1.55;
    pushStars.push({
      x: Math.cos(angle) * radius,
      y: Math.sin(angle) * radius * 0.78,
      z: 0.14 + pushNoise(index + 37) * 0.86,
      speed: 0.000045 + pushNoise(index + 47) * 0.000085,
      size: 0.55 + pushNoise(index + 59) * 1.7,
      phase: pushNoise(index + 71) * Math.PI * 2,
      color: pushPalette[Math.floor(pushNoise(index + 83) * pushPalette.length)]
    });
  }
}

function pushProject(star, elapsed, trailOffset) {
  let depth = star.z - elapsed * star.speed - trailOffset;
  depth = depth - Math.floor(depth);
  depth = 0.1 + depth * 0.9;

  const perspective = 0.28 / depth;
  const pulse = 0.72 + Math.sin(elapsed * 0.004 + star.phase) * 0.28;
  const x = 480 + star.x * 430 * perspective;
  const y = 270 + star.y * 300 * perspective;
  const near = 1 - depth;
  return {
    x: x,
    y: y,
    r: star.size * (0.45 + near * 2.65),
    opacity: pushClamp((0.08 + near * 0.72) * pulse, 0, 0.92),
    depth: depth
  };
}

function pushCreateBackground(ctx) {
  ctx.svg.background.clear();
  ctx.svg.background.rect("push-space-wash", 0, 0, 960, 540, "#070015", 0.5);
  ctx.svg.background.path(
    "push-warp-ring-a",
    "M 480 74 C 724 74 902 164 902 270 C 902 376 724 466 480 466 C 236 466 58 376 58 270 C 58 164 236 74 480 74 Z",
    "none",
    "#00f0ff",
    1.2,
    0.18);
  ctx.svg.background.path(
    "push-warp-ring-b",
    "M 480 118 C 662 118 804 186 804 270 C 804 354 662 422 480 422 C 298 422 156 354 156 270 C 156 186 298 118 480 118 Z",
    "none",
    "#ff4f81",
    1.1,
    0.13);
  ctx.svg.background.path(
    "push-warp-ring-c",
    "M 480 154 C 610 154 714 206 714 270 C 714 334 610 386 480 386 C 350 386 246 334 246 270 C 246 206 350 154 480 154 Z",
    "none",
    "#ffc000",
    1,
    0.12);

  for (let index = 0; index < pushStarCount; index++) {
    ctx.svg.background.circle("push-star-" + index, 480, 270, 0.5, pushStars[index].color, 0);
  }
}

function pushUpdateStarfield(elapsed) {
  const ringPulse = 0.45 + (Math.sin(elapsed * 0.0015) + 1) * 0.5;
  svg.id("push-warp-ring-a").set("opacity", (0.09 + ringPulse * 0.11).toFixed(3));
  svg.id("push-warp-ring-b").set("opacity", (0.06 + ringPulse * 0.1).toFixed(3));
  svg.id("push-warp-ring-c").set("opacity", (0.05 + ringPulse * 0.08).toFixed(3));
  svg.id("push-warp-ring-a").transform("rotate(" + (elapsed * 0.006).toFixed(2) + " 480 270)");
  svg.id("push-warp-ring-b").transform("rotate(" + (-elapsed * 0.004).toFixed(2) + " 480 270)");
  svg.id("push-warp-ring-c").transform("rotate(" + (elapsed * 0.003).toFixed(2) + " 480 270)");

  for (let index = 0; index < pushStarCount; index++) {
    const star = pushStars[index];
    const projected = pushProject(star, elapsed, 0);
    const visible = projected.x > -60 && projected.x < 1020 && projected.y > -60 && projected.y < 600;
    const element = svg.id("push-star-" + index);
    element.set("cx", projected.x.toFixed(2));
    element.set("cy", projected.y.toFixed(2));
    element.set("r", projected.r.toFixed(2));
    element.set("opacity", visible ? projected.opacity.toFixed(3) : "0");
  }
}

slideScript("how-far-did-we-push-it.svg", {
  enter: function (ctx) {
    pushBuildStars();
    pushCreateBackground(ctx);
    pushStarfieldActive = true;
    pushUpdateStarfield(0);
  },
  frame: function (_ctx, _time, elapsed) {
    if (!pushStarfieldActive) {
      return;
    }

    pushUpdateStarfield(elapsed);
  },
  leave: function (ctx) {
    pushStarfieldActive = false;
    ctx.svg.background.clear();
  }
});
