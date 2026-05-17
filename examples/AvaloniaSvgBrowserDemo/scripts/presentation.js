console.log("presentation.js is running inside JsEngine");

const slides = [];
slides[0] = "beyond-the-vibe-cover.svg";
slides[1] = "speaker-roger-johansson.svg";
slides[2] = "agenda-what-to-expect.svg";
slides[3] = "part-1-just-vibing.svg";
slides[4] = "rewind-november-2025.svg";
slides[5] = "social-ai-skepticism.svg";
slides[6] = "why-build-js-engine.svg";
slides[7] = "first-jsengine-prompt.svg";
slides[8] = "first-run-command-line.svg";
slides[9] = "twenty-four-hours-later.svg";
slides[10] = "test-suite-first-successes.svg";
slides[11] = "continuation-passing-style-translator.svg";
slides[12] = "initial-architecture-sexpr.svg";
slides[13] = "ast-walking-evaluation.svg";
slides[14] = "initial-itch-can-i-do-this.svg";
slides[15] = "parser-ast-runtime-redesign-map.svg";
slides[16] = "agent-observability-eyes.svg";
slides[17] = "performance-question.svg";
slides[18] = "asynkron-profiler-tool.svg";
slides[19] = "getting-stuck-limited-js-understanding.svg";
slides[20] = "goal-centered-test-ideas.svg";
slides[21] = "goal-to-tests-arrow.svg";
slides[22] = "goal-to-unknowns.svg";
slides[23] = "robot-boxing-agent-feedback.svg";
slides[24] = "two-weeks-later-found-it.svg";
slides[25] = "ecma-262-spec-angels.svg";
slides[26] = "ecma-262-94000-unit-tests.svg";
slides[27] = "test262-goal-explosion.svg";
slides[28] = "how-hard-can-it-be.svg";
slides[29] = "pretty-darn-hard.svg";
slides[30] = "annex-b-chaos.svg";
slides[31] = "run-94000-tests-ide-question.svg";
slides[32] = "immature-runtime-test262-pain.svg";
slides[33] = "test262-suite-breakdown.svg";
slides[34] = "part-2-scaling-up-fast.svg";
slides[35] = "too-many-tests-stuck.svg";
slides[36] = "testrunner-tool-teaser.svg";
slides[37] = "asynkron-testrunner-tool.svg";
slides[38] = "testrunner-bug-reports-question.svg";
slides[39] = "eighty-thousand-failing-tests.svg";
slides[40] = "ralph-loop-tool-teaser.svg";
slides[41] = "ralph-loop-pseudocode.svg";
slides[42] = "ralph-loop-autopilot.svg";
slides[43] = "bad-design-uncovered.svg";
slides[44] = "three-execution-modes-replay-generator.svg";
slides[45] = "ast-walking-cps-mode.svg";
slides[46] = "three-modes-reject-ast-walking.svg";
slides[47] = "three-modes-ir-vs-replay.svg";
slides[48] = "generator-replay-output.svg";
slides[49] = "three-modes-final-ir-only.svg";
slides[50] = "almost-done-devil.svg";
slides[51] = "make-the-real-thing-architecture.svg";
slides[52] = "execution-model-overview.svg";
slides[53] = "bytecode-stack-machine.svg";
slides[54] = "code-quality-survival-kit.svg";
slides[55] = "quickdup-duplication-radar.svg";
slides[56] = "node-host-boundary.svg";
slides[57] = "upstream-express-unchanged.svg";
slides[58] = "express-live-demo.svg";
slides[59] = "express-demo-stack.svg";
slides[60] = "jsengine-presentation-reveal.svg";

let current = presentation.current();
let phase = "idle";
let target = current;
let transitionStarted = 0;
let slideEnteredAt = 0;

const slideScripts = {};
const slideContext = {
  svg: svg,
  slide: slide,
  presentation: presentation,
  slides: slides,
  current: function () {
    return current;
  },
  file: function () {
    return slides[current];
  }
};

function slideScript(fileName, hooks) {
  slideScripts[fileName] = hooks || {};
}

function getSlideHooks(index) {
  return slideScripts[slides[index]];
}

function runSlideHook(index, name, a, b) {
  const hooks = getSlideHooks(index);
  const handler = hooks && hooks[name];
  if (typeof handler === "function") {
    return handler(slideContext, a, b);
  }

  return undefined;
}

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
  presentation.load(slides[current], current, slides.length);
  buildOverlay();
  runSlideHook(current, "enter");
  slideEnteredAt = -1;
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

  runSlideHook(current, "leave");
  phase = "fade-out";
  transitionStarted = -1;
}

slide.onFrame(function (time) {
  if (slideEnteredAt < 0) {
    slideEnteredAt = time;
  }

  const pulse = 0.55 + (Math.sin(time * 0.006) + 1) * 0.25;
  svg.id("jsengine-spark-a").set("opacity", pulse.toFixed(3));
  svg.id("jsengine-spark-a").set("r", (7 + pulse * 5).toFixed(2));
  svg.id("jsengine-spark-b").set("opacity", (0.45 + pulse * 0.32).toFixed(3));
  svg.id("jsengine-spark-c").set("opacity", (0.35 + pulse * 0.28).toFixed(3));
  runSlideHook(current, "frame", time, time - slideEnteredAt);

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
  if (runSlideHook(current, "key", "ArrowRight") === true) {
    return;
  }

  requestSlide(current + 1);
});

slide.onKey("ArrowLeft", function () {
  if (runSlideHook(current, "key", "ArrowLeft") === true) {
    return;
  }

  requestSlide(current - 1);
});

slide.onKey("Home", function () {
  if (runSlideHook(current, "key", "Home") === true) {
    return;
  }

  requestSlide(0);
});

slide.onKey("End", function () {
  if (runSlideHook(current, "key", "End") === true) {
    return;
  }

  requestSlide(slides.length - 1);
});

slide.onKey("Space", function () {
  runSlideHook(current, "key", "Space");
});

slide.onKey("R", function () {
  runSlideHook(current, "key", "R");
});

slide.onClick(function (x, y) {
  runSlideHook(current, "click", x, y);
});

function startPresentation() {
  showSlide(current);
  console.log("presentation deck loaded", slides.length, "slides");
}
