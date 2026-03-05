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

<p style="display:inline-block; background:var(--md-code-bg-color); border:1px solid var(--md-default-fg-color--lightest); border-radius:20px; padding:4px 14px; font-size:0.8rem; color:var(--md-default-fg-color--light);">Open Source · MIT License</p>

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
    var gridW = Math.ceil(W / (UNIT * COS_A)) + 16;
    var gridH = Math.ceil(H / (UNIT * SIN_A)) + 16;
    var taken = {};

    function key(r, c) { return r + ',' + c; }

    for (var row = 0; row < gridH; row++) {
      for (var col = 0; col < gridW; col++) {
        if (taken[key(row, col)]) continue;

        var rnd = Math.random();
        var size;
        if (rnd < 0.08) size = 1;
        else if (rnd < 0.2) size = 2;
        else if (rnd < 0.45) size = 3;
        else if (rnd < 0.7) size = 4;
        else if (rnd < 0.9) size = 5;
        else size = 6;

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

  function drawColumn(sx, sy, w, h, bright, alpha, highlight) {
    if (h < 1) return;
    var hw = w / 2;

    // 4 key points of the top diamond
    var topY = sy - h;           // top of column
    var lx = sx - hw * COS_A;   // left point x
    var ly = sy + hw * SIN_A;   // left point y (base)
    var rx = sx + hw * COS_A;   // right point x
    var ry = sy + hw * SIN_A;   // right point y (base)
    var by = sy + w * SIN_A;    // bottom point y (base)

    // Top diamond points
    var tlx = lx;               // top-left x
    var tly = topY + hw * SIN_A;// top-left y
    var trx = rx;               // top-right x
    var try_ = topY + hw * SIN_A;// top-right y
    var tty = topY;             // top-top y (peak of diamond)
    var tby = topY + w * SIN_A; // top-bottom y

    var a = alpha * 0.25;
    var hl = highlight ? 1 : 0;

    // Left face
    c.beginPath();
    c.moveTo(sx, tby);
    c.lineTo(lx, tly);
    c.lineTo(lx, ly);
    c.lineTo(sx, by);
    c.closePath();
    c.fillStyle = hl ? 'rgba(20,60,140,' + alpha * 0.7 + ')' : 'rgba(8,18,40,' + a * 0.25 + ')';
    c.fill();
    c.strokeStyle = hl ? 'rgba(60,150,255,' + alpha * 0.9 + ')' : 'rgba(30,70,140,' + a * 0.6 + ')';
    c.lineWidth = hl ? 1 : 0.5;
    c.stroke();

    // Right face
    c.beginPath();
    c.moveTo(sx, tby);
    c.lineTo(rx, try_);
    c.lineTo(rx, ry);
    c.lineTo(sx, by);
    c.closePath();
    c.fillStyle = hl ? 'rgba(30,80,180,' + alpha * 0.75 + ')' : 'rgba(18,45,95,' + a * 0.3 + ')';
    c.fill();
    c.strokeStyle = hl ? 'rgba(70,160,255,' + alpha * 0.9 + ')' : 'rgba(40,100,190,' + a * 0.7 + ')';
    c.stroke();

    // Top face
    c.beginPath();
    c.moveTo(sx, tty);
    c.lineTo(rx, try_);
    c.lineTo(sx, tby);
    c.lineTo(lx, tly);
    c.closePath();
    c.fillStyle = hl ? 'rgba(50,130,230,' + alpha * 0.85 + ')' : 'rgba(35,90,175,' + a * 0.35 + ')';
    c.fill();
    c.strokeStyle = hl ? 'rgba(100,190,255,' + alpha + ')' : 'rgba(70,160,255,' + a * 0.8 + ')';
    c.stroke();

    // Glow + fade-down for highlights
    if (hl) {
      c.shadowColor = 'rgba(60,150,255,0.4)';
      c.shadowBlur = 12;
      c.beginPath();
      c.moveTo(sx, tty);
      c.lineTo(rx, try_);
      c.lineTo(sx, tby);
      c.lineTo(lx, tly);
      c.closePath();
      c.fill();
      c.shadowBlur = 0;

      // Fade-out glow below the cube
      var fadeH = h * 1.5;
      var grad = c.createLinearGradient(sx, by, sx, by + fadeH);
      grad.addColorStop(0, 'rgba(50,130,230,' + alpha * 0.4 + ')');
      grad.addColorStop(1, 'rgba(50,130,230,0)');

      // Left fade
      c.beginPath();
      c.moveTo(lx, ly);
      c.lineTo(sx, by);
      c.lineTo(sx, by + fadeH);
      c.lineTo(lx, ly + fadeH);
      c.closePath();
      c.fillStyle = grad;
      c.fill();

      // Right fade
      c.beginPath();
      c.moveTo(sx, by);
      c.lineTo(rx, ry);
      c.lineTo(rx, ry + fadeH);
      c.lineTo(sx, by + fadeH);
      c.closePath();
      c.fillStyle = grad;
      c.fill();
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
    var count = 3 + Math.floor(Math.random() * 5);
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

    // Shuffle highlights every 1s
    if (ts - lastHL > 1000) {
      shuffleHighlights();
      lastHL = ts;
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
      drawColumn(sx, sy, cube.w, colH, bright, alpha, cube.highlight);
    }

    requestAnimationFrame(draw);
  }
  requestAnimationFrame(draw);
})();
</script>
