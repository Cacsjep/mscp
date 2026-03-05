---
hide:
  - navigation
  - toc
---

<style>
  .md-content__button { display: none; }
  .hero .headerlink { display: none; }

  .hero {
    position: relative;
    text-align: center;
    padding: 6rem 1rem 5rem;
    overflow: hidden;
  }
  .md-content__inner {
    margin: 0 !important;
    padding: 0 !important;
    max-width: 100% !important;
  }
  .md-main__inner {
    margin: 0 !important;
    max-width: 100% !important;
  }
  .md-content {
    max-width: 100% !important;
    overflow-x: hidden !important;
  }
  .hero > * {
    position: relative;
    z-index: 1;
  }
  #hero-bg {
    position: absolute !important;
    top: 0; left: 0;
    width: 100%; height: 100%;
    z-index: 0 !important;
    pointer-events: none;
  }
  .hero h1 {
    font-size: 2.8rem;
    font-weight: 800;
    margin-bottom: 0.5rem;
    color: #ffffff;
  }
  .hero h1 span {
    color: var(--md-primary-fg-color);
  }
  .hero .subtitle {
    color: var(--md-default-fg-color--light);
    font-size: 1.15rem;
  }
  .hero-buttons {
    display: flex;
    gap: 12px;
    justify-content: center;
    flex-wrap: wrap;
    margin-bottom: 1.5rem;
  }
  .hero-buttons .md-button {
    font-weight: 600;
  }
  .disclaimer {
    font-size: 0.78rem;
    color: var(--md-default-fg-color--lighter);
    max-width: 520px;
    margin: 0 auto;
  }

  @media (max-width: 700px) {
    .hero h1 { font-size: 2rem; }
  }
</style>

<div class="hero" markdown>

# Community Plugins for<br>Milestone <span>XProtect™</span>

<p class="subtitle">A collection of open-source plugins and drivers for Milestone XProtect™.</p>

<div class="hero-buttons" markdown>

[:material-download: Download Installer](getting-started/installation.md){ .md-button .md-button--primary }

</div>

<p class="disclaimer">This is an independent open source project and is not affiliated with, endorsed by, or supported by Milestone Systems. XProtect™ is a trademark of Milestone Systems A/S.</p>

</div>

<script>
(function(){
  var hero = document.querySelector('.hero');
  if (!hero) return;

  var cv = document.createElement('canvas');
  cv.id = 'hero-bg';
  hero.insertBefore(cv, hero.firstChild);

  var c = cv.getContext('2d');
  var W, H;

  var ISO_ANGLE = Math.PI / 6;
  var COS_A = Math.cos(ISO_ANGLE);
  var SIN_A = Math.sin(ISO_ANGLE);

  // ── Place cubes randomly with varying sizes ──
  var cubes = [];
  var UNIT = 22; // grid unit

  function buildCubes() {
    cubes = [];
    var gridW = Math.ceil(W / (UNIT * COS_A)) + 24;
    var gridH = Math.ceil(H / (UNIT * SIN_A)) + 24;
    var taken = {};

    function key(r, c) { return r + ',' + c; }

    for (var row = 0; row < gridH; row++) {
      for (var col = 0; col < gridW; col++) {
        if (taken[key(row, col)]) continue;

        var rnd = Math.random();
        var size;
        if (rnd < 0.05) size = 1;
        else if (rnd < 0.12) size = 2;
        else if (rnd < 0.22) size = 3;
        else if (rnd < 0.35) size = 4;
        else if (rnd < 0.48) size = 5;
        else if (rnd < 0.60) size = 6;
        else if (rnd < 0.72) size = 7;
        else if (rnd < 0.82) size = 8;
        else if (rnd < 0.92) size = 9;
        else size = 10;

        // Check if space is free
        var fits = true;
        for (var dr = 0; dr < size && fits; dr++) {
          for (var dc = 0; dc < size && fits; dc++) {
            if (taken[key(row + dr, col + dc)]) fits = false;
          }
        }
        if (!fits) { size = 1; } // fallback to 1x1

        // Mark cells taken
        for (var dr = 0; dr < size; dr++) {
          for (var dc = 0; dc < size; dc++) {
            taken[key(row + dr, col + dc)] = true;
          }
        }

        var gx = (col - gridW / 2);
        var gz = (row - gridH / 2);
        var cubeW = size * UNIT;

        cubes.push({
          gx: gx,
          gz: gz,
          size: size,
          w: cubeW,
          phase: Math.random() * Math.PI * 2,
          speed: 0.4 + Math.random() * 0.8,
          baseH: 0.2 + Math.random() * 0.8,
          highlight: false
        });
      }
    }

    // Sort back-to-front for painter's algorithm
    cubes.sort(function(a, b) {
      return (a.gx + a.gz) - (b.gx + b.gz);
    });
  }

  function resize() {
    W = cv.width = hero.offsetWidth;
    H = cv.height = hero.offsetHeight;
    buildCubes();
  }
  resize();
  addEventListener('resize', resize);

  var offsetX = 0, offsetY = 0;

  // Wave from bottom-left to top-right (along gx+gz diagonal)
  function waveY(gx, gz, t) {
    var d = gx + gz; // diagonal axis: bottom-left to top-right
    var w1 = Math.sin(d * 0.06 - t * 0.5);
    var w2 = Math.sin(d * 0.1 - t * 0.7) * 0.4;
    return (w1 + w2) * 10;
  }

  function drawColumn(sx, sy, w, h, bright, alpha, hl) {
    if (h < 1) return;
    var hw = w / 2;

    var topY = sy - h;
    var lx = sx - hw * COS_A;
    var ly = sy + hw * SIN_A;
    var rx = sx + hw * COS_A;
    var ry = sy + hw * SIN_A;
    var by = sy + w * SIN_A;

    var tly = topY + hw * SIN_A;
    var try_ = topY + hw * SIN_A;
    var tty = topY;
    var tby = topY + w * SIN_A;

    var a = alpha * 0.25;

    // Left face
    c.beginPath();
    c.moveTo(sx, tby); c.lineTo(lx, tly); c.lineTo(lx, ly); c.lineTo(sx, by);
    c.closePath();
    c.fillStyle = 'rgba(8,18,40,' + a * 0.25 + ')';
    c.fill();
    c.strokeStyle = 'rgba(30,70,140,' + a * 0.6 + ')';
    c.lineWidth = 0.5;
    c.stroke();

    // Right face
    c.beginPath();
    c.moveTo(sx, tby); c.lineTo(rx, try_); c.lineTo(rx, ry); c.lineTo(sx, by);
    c.closePath();
    c.fillStyle = 'rgba(18,45,95,' + a * 0.3 + ')';
    c.fill();
    c.strokeStyle = 'rgba(40,100,190,' + a * 0.7 + ')';
    c.stroke();

    // Top face — glassy look with gradient
    c.beginPath();
    c.moveTo(sx, tty); c.lineTo(rx, try_); c.lineTo(sx, tby); c.lineTo(lx, tly);
    c.closePath();
    var topGrad = c.createLinearGradient(lx, tty, rx, tby);
    var baseA = a * 0.35 + hl * alpha * 0.6;
    topGrad.addColorStop(0, 'rgba(60,140,220,' + baseA * 1.2 + ')');
    topGrad.addColorStop(0.4, 'rgba(35,90,175,' + baseA * 0.7 + ')');
    topGrad.addColorStop(1, 'rgba(20,60,130,' + baseA * 0.4 + ')');
    c.fillStyle = topGrad;
    c.fill();
    c.strokeStyle = 'rgba(120,200,255,' + (a * 0.5 + hl * alpha * 0.6) + ')';
    c.lineWidth = 0.5 + hl;
    c.stroke();

    // Highlight: glow all cube edges
    if (hl > 0.01) {
      var edgeA = hl * alpha * 0.9;
      c.strokeStyle = 'rgba(100,180,255,' + edgeA + ')';
      c.lineWidth = 0.5 + hl * 0.5;
      c.shadowColor = 'rgba(60,150,255,' + hl * 0.5 + ')';
      c.shadowBlur = 10 * hl;

      // Top face edges
      c.beginPath();
      c.moveTo(sx, tty); c.lineTo(rx, try_); c.lineTo(sx, tby); c.lineTo(lx, tly);
      c.closePath();
      c.stroke();

      // Left face edges
      c.beginPath();
      c.moveTo(sx, tby); c.lineTo(lx, tly); c.lineTo(lx, ly); c.lineTo(sx, by);
      c.closePath();
      c.stroke();

      // Right face edges
      c.beginPath();
      c.moveTo(sx, tby); c.lineTo(rx, try_); c.lineTo(rx, ry); c.lineTo(sx, by);
      c.closePath();
      c.stroke();

      c.shadowBlur = 0;
    }
  }

  var t = 0;
  var lastHL = 0;
  var hlFade = 0; // 0..1 fade intensity for highlights
  var HL_CYCLE = 3000; // total cycle ms
  var HL_FADE = 500;   // fade in/out ms

  function shuffleHighlights() {
    for (var i = 0; i < cubes.length; i++) cubes[i].highlight = false;
    var edge = [];
    for (var i = 0; i < cubes.length; i++) {
      var cb = cubes[i];
      var centerGx = cb.gx + cb.size * 0.5;
      var centerGz = cb.gz + cb.size * 0.5;
      var sx = (centerGx - centerGz) * UNIT * COS_A;
      if (Math.abs(sx) > W * 0.2) edge.push(cb);
    }
    var count = 25 + Math.floor(Math.random() * 16);
    for (var i = 0; i < count && edge.length > 0; i++) {
      var idx = Math.floor(Math.random() * edge.length);
      edge[idx].highlight = true;
      edge.splice(idx, 1);
    }
  }

  // Pre-allocate draw list
  var drawList = [];

  function draw(ts) {
    t = ts * 0.001;

    // Highlight cycle: fade in 500ms, hold, fade out 500ms, switch
    var elapsed = ts - lastHL;
    if (elapsed > HL_CYCLE) {
      shuffleHighlights();
      lastHL = ts;
      elapsed = 0;
    }
    if (elapsed < HL_FADE) {
      hlFade = elapsed / HL_FADE;
    } else if (elapsed > HL_CYCLE - HL_FADE) {
      hlFade = (HL_CYCLE - elapsed) / HL_FADE;
    } else {
      hlFade = 1;
    }
    c.clearRect(0, 0, W, H);

    offsetX = W / 2;
    offsetY = H * 0.38;

    // Compute screen positions + sort
    drawList.length = 0;
    for (var i = 0; i < cubes.length; i++) {
      var cube = cubes[i];
      var centerGx = cube.gx + cube.size * 0.5;
      var centerGz = cube.gz + cube.size * 0.5;
      var sx = (centerGx - centerGz) * UNIT * COS_A + offsetX;
      var wy = waveY(cube.gx, cube.gz, t);
      var baseY = (centerGx + centerGz) * UNIT * SIN_A + offsetY;
      var sy = baseY - wy;

      if (sx < -80 || sx > W + 80 || sy < -100 || sy > H + 80) continue;

      drawList.push({ cube: cube, sx: sx, sy: sy, wy: wy, baseY: baseY });
    }

    // Sort by baseY (grid depth) so back cubes draw first
    drawList.sort(function(a, b) { return a.baseY - b.baseY; });

    for (var i = 0; i < drawList.length; i++) {
      var d = drawList[i];
      var cube = d.cube;
      var sx = d.sx, sy = d.sy, wy = d.wy;
      var colH = (5 + cube.baseH * 25) * (0.5 + cube.size * 0.25);

      // Vignette
      var dx = (sx - W / 2) / (W / 2);
      var dy = (sy - H / 2) / (H / 2);
      var edgeFade = 1 - Math.sqrt(dx * dx * 0.5 + dy * dy * 0.7);
      edgeFade = Math.max(0, Math.min(1, edgeFade));

      // Dim center for text
      var cx2 = Math.abs(sx - W / 2) / (W * 0.25);
      var cy2 = Math.abs(sy - H / 2) / (H * 0.28);
      var centerDim = Math.max(0.12, 1 - Math.max(0, 1 - Math.sqrt(cx2 * cx2 + cy2 * cy2)));

      var alpha = edgeFade * centerDim * 0.9;
      if (alpha < 0.01) continue;

      var bright = 0.4 + cube.baseH * 0.3 + Math.max(0, wy / 20) * 0.3;
      var hl = cube.highlight ? hlFade : 0;
      drawColumn(sx, sy, cube.w, colH, bright, alpha, hl);
    }

    requestAnimationFrame(draw);
  }
  requestAnimationFrame(draw);
})();
</script>
