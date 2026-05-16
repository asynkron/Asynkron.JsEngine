slideScript("ralph-loop-autopilot.svg", {
  enter: function (ctx) {
    ctx.svg.layer.text("ralph-loop-label", "agent loop live", 82, 628, 28, "#00b0f0", 0.86);
    ctx.svg.layer.circle("ralph-loop-dot-a", 870, 168, 7, "#ffc000", 0.95);
    ctx.svg.layer.circle("ralph-loop-dot-b", 1038, 324, 6, "#00b0f0", 0.82);
    ctx.svg.layer.circle("ralph-loop-dot-c", 832, 520, 5, "#ff4f81", 0.78);
  },

  frame: function (ctx, time, elapsed) {
    const lap = elapsed * 0.0024;
    const flicker = 0.58 + (Math.sin(elapsed * 0.011) + 1) * 0.18;

    ctx.svg.id("ralph-loop-label").set("opacity", flicker.toFixed(3));
    ctx.svg.id("ralph-loop-dot-a").set("cx", (948 + Math.cos(lap) * 182).toFixed(2));
    ctx.svg.id("ralph-loop-dot-a").set("cy", (338 + Math.sin(lap) * 172).toFixed(2));
    ctx.svg.id("ralph-loop-dot-b").set("cx", (948 + Math.cos(lap + 2.1) * 182).toFixed(2));
    ctx.svg.id("ralph-loop-dot-b").set("cy", (338 + Math.sin(lap + 2.1) * 172).toFixed(2));
    ctx.svg.id("ralph-loop-dot-c").set("cx", (948 + Math.cos(lap + 4.2) * 182).toFixed(2));
    ctx.svg.id("ralph-loop-dot-c").set("cy", (338 + Math.sin(lap + 4.2) * 172).toFixed(2));
  }
});
