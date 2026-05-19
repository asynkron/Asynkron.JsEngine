const faktorialLaunchButton = { x: 284, y: 231, w: 392, h: 78 };

function faktorialLaunchContains(x, y) {
  return x >= faktorialLaunchButton.x &&
    x <= faktorialLaunchButton.x + faktorialLaunchButton.w &&
    y >= faktorialLaunchButton.y &&
    y <= faktorialLaunchButton.y + faktorialLaunchButton.h;
}

slideScript("faktorial-launch-button.svg", {
  frame: function (ctx, _time, elapsed) {
    const pulse = (Math.sin(elapsed * 0.004) + 1) / 2;
    const scale = 0.92 + pulse * 0.16;

    ctx.svg.id("faktorial-glow").set("opacity", (0.50 + pulse * 0.24).toFixed(3));
    ctx.svg.id("faktorial-glow").transform("translate(480 270) scale(" + scale.toFixed(3) + " " + scale.toFixed(3) + ") translate(-480 -270)");
  },

  click: function (_ctx, x, y) {
    if (!faktorialLaunchContains(x, y)) {
      return false;
    }

    demo.startFaktorialQueue();
    return true;
  }
});
