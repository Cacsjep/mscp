---
title: "Changelog - Milestone XProtect Plugins"
description: "Changelog and release notes for MS Community Plugins — version history for all Milestone XProtect plugins and drivers."
hide:
  - navigation
  - toc
---

<style>
  /* ── Changelog list as rows ── */
  .changelog-body ul {
    list-style: none !important;
    margin-left: 0 !important;
    padding-left: 0 !important;
  }
  .changelog-body li {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 6px 8px;
    padding: 7px 4px;
    margin: 0 !important;
    line-height: 1.45;
  }
  .changelog-body li::before { content: none !important; }

  .cl-badge {
    flex-shrink: 0;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    width: 1.45rem;
    height: 1.45rem;
    border-radius: 5px;
    font-size: 0.9rem;
    line-height: 1;
  }
  .cl-badge.added    { background: rgba(63,185,80,0.15);  color: #3fb950; }
  .cl-badge.improved { background: rgba(88,166,255,0.15); color: #58a6ff; }
  .cl-badge.fixed    { background: rgba(248,81,73,0.15);  color: #f85149; }
  .cl-badge.security { background: rgba(227,179,65,0.18); color: #e3b341; }
  .cl-badge.other    { background: rgba(139,148,158,0.15); color: #8b949e; }

  .cl-comp {
    flex-shrink: 0;
    display: inline-block;
    font-size: 0.52rem;
    font-weight: 600;
    padding: 2px 8px;
    border-radius: 5px;
    border: 1px solid var(--md-default-fg-color--lightest);
    background: #111;
    color: var(--md-default-fg-color--light);
  }

  /* Description sits on its own line, under the badge + chip */
  .cl-text {
    flex: 1 1 100%;
    margin-top: 2px;
    font-size: 0.62rem;
  }

  /* No permalink pilcrows on this page */
  .changelog-body .headerlink { display: none !important; }

  /* ── Version headers ── */
  .changelog-body h2 {
    border: none;
    margin: 1.8rem 0 0.4rem;
    padding-top: 0.6rem;
  }
  .changelog-body .cl-ver { font-weight: 800; }
  .changelog-body .cl-date {
    margin-left: 10px;
    font-size: 0.7rem;
    font-weight: 400;
    color: var(--md-default-fg-color--light);
  }
</style>

<link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/@mdi/font@7/css/materialdesignicons.min.css">

<div class="show-title" markdown>

--8<-- "CHANGELOG.md"

</div>

<script>
(function () {
  var root = document.querySelector('.show-title');
  if (!root) return;
  root.classList.add('changelog-body');

  var LABEL = {
    add: 'Added', fix: 'Fixed', improve: 'Improved', remove: 'Removed',
    security: 'Security', change: 'Changed', update: 'Updated',
    maintenance: 'Chore', bump: 'Chore', deprecate: 'Deprecated'
  };
  var BUCKET = {
    add: 'added', fix: 'fixed', improve: 'improved', remove: 'other',
    security: 'security', change: 'other', update: 'other',
    maintenance: 'other', bump: 'other', deprecate: 'other'
  };
  var ICON = {
    add: 'mdi-test-tube', fix: 'mdi-bug', improve: 'mdi-auto-fix',
    remove: 'mdi-minus-circle', security: 'mdi-shield-lock',
    change: 'mdi-swap-horizontal', update: 'mdi-update',
    maintenance: 'mdi-cog', bump: 'mdi-cog', deprecate: 'mdi-cancel'
  };
  var RE = /^\s*(Add|Fix|Improve|Remove|Security|Change|Update|Maintenance|Bump|Deprecate)\b[ \t]*([^:<]*?):[ \t]*/;

  root.querySelectorAll('li').forEach(function (li) {
    var html = li.innerHTML;
    var m = html.match(RE);
    var verb = m ? m[1].toLowerCase() : null;
    var bucket = verb ? BUCKET[verb] : 'other';
    var comp = m ? (m[2] || '').trim() : '';
    var rest = m ? html.slice(m[0].length) : html;

    var icon = (verb && ICON[verb]) || 'mdi-circle-medium';
    var title = verb ? LABEL[verb] : 'Note';
    var badge = '<span class="cl-badge ' + bucket + '" title="' + title + '"><i class="mdi ' + icon + '"></i></span>';
    var chip = comp ? '<span class="cl-comp">' + comp + '</span>' : '';
    li.innerHTML = badge + chip + '<span class="cl-text">' + rest + '</span>';
  });

  // Reformat "[3.4.11] - 2026-06-06" -> v3.4.11 · 2026-06-06
  root.querySelectorAll('h2').forEach(function (h) {
    var link = h.querySelector('.headerlink');
    if (link) link.parentNode.removeChild(link);
    var t = h.textContent.trim();
    var mm = t.match(/^\[?\s*([0-9][^\]]*?)\s*\]?\s*[-–]\s*(.+)$/);
    if (mm) {
      h.innerHTML = '<span class="cl-ver">v' + mm[1] + '</span><span class="cl-date">' + mm[2] + '</span>';
    }
  });
})();
</script>
