console.log("presentation.js is running inside JsEngine");

const slides = [];
for (let index = 0; index < presentation.count(); index++) {
  slides.push(presentation.path(index));
}

let current = presentation.current();
let phase = "idle";
let target = current;
let transitionStarted = 0;

function setCaption() {
  svg.layer.text(
    "jsengine-caption",
    "JsEngine controls this deck · " + (current + 1) + " / " + slides.length,
    54,
    681,
    22,
    "#ffffff",
    0.78
  );
}

function addSparkle() {
  const x = 1110 + Math.sin(current * 1.7) * 56;
  const y = 78 + Math.cos(current * 1.2) * 24;

  svg.layer.circle("jsengine-spark-a", x, y, 8, "#ffc000", 0.86);
  svg.layer.circle("jsengine-spark-b", x + 26, y + 18, 4, "#00b0f0", 0.75);
  svg.layer.circle("jsengine-spark-c", x - 18, y + 28, 3, "#ff4f81", 0.65);
}

function buildOverlay() {
  svg.layer.clear();
  svg.layer.rect("jsengine-fade", 0, 0, 1280, 720, "#05070d", 0);
  setCaption();
  addSparkle();
}

function showSlide(index) {
  if (slides.length === 0) {
    return;
  }

  current = (index + slides.length) % slides.length;
  presentation.load(current);
  buildOverlay();
  console.log("slide", current + 1, "of", slides.length, slides[current]);
}

function requestSlide(index) {
  if (phase !== "idle" || slides.length === 0) {
    return;
  }

  target = (index + slides.length) % slides.length;
  if (target === current) {
    return;
  }

  phase = "fade-out";
  transitionStarted = -1;
}

slide.onFrame(function (time) {
  const pulse = 0.55 + (Math.sin(time * 0.006) + 1) * 0.25;
  svg.id("jsengine-spark-a").set("opacity", pulse.toFixed(3));
  svg.id("jsengine-spark-a").set("r", (7 + pulse * 5).toFixed(2));
  svg.id("jsengine-spark-b").set("opacity", (0.45 + pulse * 0.32).toFixed(3));
  svg.id("jsengine-spark-c").set("opacity", (0.35 + pulse * 0.28).toFixed(3));

  if (phase === "idle") {
    return;
  }

  if (transitionStarted < 0) {
    transitionStarted = time;
  }

  const elapsed = time - transitionStarted;
  if (phase === "fade-out") {
    const amount = Math.min(1, elapsed / 170);
    svg.id("jsengine-fade").set("opacity", amount.toFixed(3));
    if (amount >= 1) {
      showSlide(target);
      svg.id("jsengine-fade").set("opacity", "1");
      phase = "fade-in";
      transitionStarted = time;
    }
    return;
  }

  const amount = Math.max(0, 1 - elapsed / 220);
  svg.id("jsengine-fade").set("opacity", amount.toFixed(3));
  if (amount <= 0) {
    phase = "idle";
  }
});

slide.onKey("ArrowRight", function () {
  requestSlide(current + 1);
});

slide.onKey("ArrowLeft", function () {
  requestSlide(current - 1);
});

slide.onKey("Home", function () {
  requestSlide(0);
});

slide.onKey("End", function () {
  requestSlide(slides.length - 1);
});

buildOverlay();
console.log("presentation deck loaded", slides.length, "slides");
