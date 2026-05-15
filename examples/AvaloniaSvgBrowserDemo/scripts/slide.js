console.log("slide.js is running inside JsEngine");

const headline = svg.id("headline");
const subtitle = svg.id("subtitle");
const pulse = svg.id("pulse");
const beam = svg.id("beam");
const runtime = svg.id("runtime");
const svgModel = svg.id("svgmodel");

headline.set("fill", "#00b0f0");
subtitle.set("opacity", "0.72");

slide.onFrame(function (time) {
  const wave = Math.sin(time * 0.004);
  const glow = 0.35 + (wave + 1) * 0.3;
  const travel = Math.sin(time * 0.002) * 22;

  pulse.set("opacity", glow.toFixed(3));
  pulse.set("r", (66 + glow * 38).toFixed(1));
  beam.set("opacity", (0.35 + glow * 0.45).toFixed(3));
  runtime.transform("translate(" + (120 + travel).toFixed(1) + " 430)");
  svgModel.transform("translate(" + (888 - travel).toFixed(1) + " 430)");
});

slide.onKey("Space", function () {
  headline.set("fill", "#ffc000");
  subtitle.text("Space was handled by JsEngine.");
});

slide.onKey("R", function () {
  headline.set("fill", "#00b0f0");
  subtitle.text("A tiny custom SVG API, not a full browser DOM.");
});
