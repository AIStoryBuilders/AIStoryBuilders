// AI StoryBuilders browser helpers, ported from KnowledgeGraphBuilder's kgb.js (plan §5.7).
// Loaded by both hosts before the Blazor script. CSS class names keep KGB's kgb- prefix.
window.asb = (function () {
    let unsaved = false;
    let shortcutTarget = null;

    window.addEventListener('beforeunload', function (e) {
        if (unsaved) {
            e.preventDefault();
            e.returnValue = '';
        }
    });

    document.addEventListener('keydown', function (e) {
        if (!shortcutTarget) return;
        const key = e.key;
        const ctrl = e.ctrlKey || e.metaKey;
        if (ctrl && (key === 's' || key === 'S')) {
            e.preventDefault();
            shortcutTarget.invokeMethodAsync('OnShortcut', 'save');
        }
    }, true);

    return {
        noop: function () { },

        setUnsaved: function (value) { unsaved = !!value; },

        withLock: function (name, target) {
            if (!navigator.locks) throw new Error('Web Locks are not supported by this browser.');
            return navigator.locks.request(name, function () {
                return target.invokeMethodAsync('RunAsync');
            });
        },

        registerShortcuts: function (target) { shortcutTarget = target; },

        // Returns a handle: isWide() and dispose(). The caller disposes it when its component goes away (plan §5.7).
        watchWidth: function (target, breakpoint) {
            const mq = window.matchMedia('(min-width: ' + breakpoint + 'px)');
            const handler = function (ev) { target.invokeMethodAsync('OnWidthChanged', ev.matches); };
            if (mq.addEventListener) mq.addEventListener('change', handler); else mq.addListener(handler);
            return {
                isWide: function () { return mq.matches; },
                dispose: function () { if (mq.removeEventListener) mq.removeEventListener('change', handler); else mq.removeListener(handler); }
            };
        },

        focus: function (selector) {
            const el = document.querySelector(selector);
            if (el) el.focus();
        },

        scrollToBottom: function (selector) {
            const el = document.querySelector(selector);
            if (el) el.scrollTop = el.scrollHeight;
        },

        // Browser host only: forward localStorage changes made by other tabs (D18).
        registerStorageListener: function (target, prefix) {
            window.addEventListener('storage', function (e) {
                if (e.storageArea !== window.localStorage) return;
                if (e.key === null || e.key.startsWith(prefix)) {
                    target.invokeMethodAsync('OnStorageChanged', e.key);
                }
            });
        },

        storageGet: function (key) { return window.localStorage.getItem(key); },

        // Browser host only: report the tab going to the background so the open story can be flushed (§5.5 step 4).
        // Best effort: the browser does not wait for the async .NET call to finish.
        registerVisibilityListener: function (target) {
            document.addEventListener('visibilitychange', function () {
                if (document.visibilityState === 'hidden') target.invokeMethodAsync('OnHidden');
            });
        },

        scrollIntoView: function (selector, block) {
            const el = document.querySelector(selector);
            if (el) el.scrollIntoView({ behavior: 'smooth', block: block || 'start' });
        },

        saveAsFile: async function (fileName, streamRef, contentType) {
            const buffer = await streamRef.arrayBuffer();
            const blob = new Blob([buffer], { type: contentType || 'application/octet-stream' });
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url;
            a.download = fileName;
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
        }
    };
})();

// Explorer drag and drop with pointer events. HTML5 drag and drop doesn't work inside MAUI's WebView2 on Windows
// (the drag ends as soon as it starts), so both hosts use this. Items carry data-kgb-path; folders and the root
// drop zone also carry data-kgb-drop. The target sent to .NET is a folder path, "" for the root, or null for none.
window.asb.bindTreeDrag = function (root, target) {
    if (!root || root.dataset.kgbDrag) return;
    root.dataset.kgbDrag = '1';
    let start = null, dragging = false, over;

    function dropTargetAt(x, y) {
        const el = document.elementFromPoint(x, y);
        const hit = el && root.contains(el) ? el.closest('[data-kgb-drop]') : null;
        return hit ? hit.dataset.kgbPath : null;
    }
    function finish(drop) {
        document.removeEventListener('pointermove', onMove, true);
        document.removeEventListener('pointerup', onUp, true);
        document.removeEventListener('keydown', onKey, true);
        if (dragging) {
            target.invokeMethodAsync('OnPointerDrop', drop ? over : null);
            // The pointerup ends with a click on the item under it; don't let that open or select it.
            const swallow = function (e) { e.stopPropagation(); e.preventDefault(); };
            document.addEventListener('click', swallow, { capture: true, once: true });
            setTimeout(function () { document.removeEventListener('click', swallow, true); }, 0);
        }
        start = null; dragging = false; over = undefined;
    }
    function onMove(e) {
        if (!start) return;
        if (!dragging) {
            if (Math.abs(e.clientX - start.x) + Math.abs(e.clientY - start.y) < 6) return;
            dragging = true;
            target.invokeMethodAsync('OnPointerDragStart', start.path);
        }
        const next = dropTargetAt(e.clientX, e.clientY);
        if (next !== over) {
            over = next;
            target.invokeMethodAsync('OnPointerDragOver', next);
        }
    }
    function onUp() { finish(true); }
    function onKey(e) { if (e.key === 'Escape') { e.preventDefault(); finish(false); } }

    // F2 and Delete. RadzenTree stops keydown propagation inside Blazor, so a Blazor @onkeydown on an ancestor never runs.
    root.addEventListener('keydown', function (e) {
        if ((e.key === 'F2' || e.key === 'Delete') && !e.target.closest('input, textarea, [contenteditable]')) {
            e.preventDefault();
            target.invokeMethodAsync('OnTreeKey', e.key);
        }
    });

    root.addEventListener('pointerdown', function (e) {
        if (e.button !== 0) return;
        const item = e.target.closest('[data-kgb-path]');
        if (!item || !root.contains(item) || item.dataset.kgbPath === '') return;
        start = { x: e.clientX, y: e.clientY, path: item.dataset.kgbPath };
        document.addEventListener('pointermove', onMove, true);
        document.addEventListener('pointerup', onUp, true);
        document.addEventListener('keydown', onKey, true);
    });
};

// Graph page: wheel zoom around the pointer, drag the background to pan (both by changing the SVG viewBox), drag a
// node to move it, and hover highlighting of a node's links and neighbors. Blazor re-renders the SVG's children, so
// state lives on the element, and a rebuilt layout (a new data-home from .NET) resets the view. A moved node is
// reported to .NET (OnNodeMoved) so the next render keeps it where it was dropped.
window.asb.bindGraph = function (svg, target) {
    if (!svg) return null;
    const home = svg.dataset.home || svg.getAttribute('viewBox');
    if (svg.kgbGraph) {
        svg.kgbGraph.target = target;
        if (svg.kgbGraph.home !== home) { svg.kgbGraph.home = home; svg.kgbGraph.view = parse(home); svg.kgbGraph.apply(); }
        return svg.kgbGraph.handle;
    }
    // Every listener goes through on(), so dispose() removes them all, including the window ones (plan §5.7).
    const off = [];
    function on(el, type, fn, opts) {
        el.addEventListener(type, fn, opts);
        off.push(function () { el.removeEventListener(type, fn, opts); });
    }
    function parse(v) { const p = v.split(/\s+/).map(Number); return { x: p[0], y: p[1], w: p[2], h: p[3] }; }
    const state = svg.kgbGraph = { home: home, view: parse(home), drag: null, node: null, moved: false, target: target };
    // --gz is the zoom relative to the fitted view: labels divide their size by it so they stay readable, and
    // zooming in far enough labels every node.
    function apply() {
        const v = state.view;
        svg.setAttribute('viewBox', v.x + ' ' + v.y + ' ' + v.w + ' ' + v.h);
        const z = parse(state.home).w / v.w;
        svg.style.setProperty('--gz', z.toFixed(3));
        svg.classList.toggle('zoomed', z >= 1.8);
    }
    state.apply = apply;
    apply();
    function toGraph(e) {
        const r = svg.getBoundingClientRect(), v = state.view;
        // preserveAspectRatio "xMidYMid meet": one scale for both axes, centered.
        const s = Math.min(r.width / v.w, r.height / v.h);
        return { x: v.x + (e.clientX - r.left - (r.width - v.w * s) / 2) / s, y: v.y + (e.clientY - r.top - (r.height - v.h * s) / 2) / s, s: s };
    }
    function position(g) {
        const m = /translate\(\s*([-\d.e]+)[ ,]+([-\d.e]+)/.exec(g.getAttribute('transform') || '');
        return m ? { x: +m[1], y: +m[2] } : { x: 0, y: 0 };
    }
    function radius(g) { const c = g.querySelector('circle'); return c ? +c.getAttribute('r') : 0; }
    function nodeById(id) {
        for (const g of svg.querySelectorAll('.kgb-gnode')) if (g.dataset.id === id) return g;
        return null;
    }
    // Same trimming as the .NET side: lines start and end at the circles' edges.
    function placeLine(l, a, ra, b, rb) {
        const dx = b.x - a.x, dy = b.y - a.y, len = Math.sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return;
        const ux = dx / len, uy = dy / len;
        l.setAttribute('x1', a.x + ux * ra); l.setAttribute('y1', a.y + uy * ra);
        l.setAttribute('x2', b.x - ux * (rb + 2)); l.setAttribute('y2', b.y - uy * (rb + 2));
    }

    on(svg, 'wheel', function (e) {
        e.preventDefault();
        const p = toGraph(e), v = state.view, home = parse(state.home);
        const factor = Math.exp(Math.sign(e.deltaY) * 0.15);
        const w = Math.min(home.w * 4, Math.max(home.w / 20, v.w * factor));
        const k = w / v.w;
        state.view = { x: p.x - (p.x - v.x) * k, y: p.y - (p.y - v.y) * k, w: w, h: v.h * k };
        apply();
    }, { passive: false });

    on(svg, 'pointerdown', function (e) {
        if (e.button !== 0) return;
        const p = toGraph(e);
        state.moved = false;
        const g = e.target.closest('.kgb-gnode');
        if (g) {
            // Remember the node, its neighbors' positions and its lines, so a move only touches those elements.
            const id = g.dataset.id, lines = [];
            svg.querySelectorAll('.kgb-gedge').forEach(function (l) {
                const other = l.dataset.from === id ? l.dataset.to : l.dataset.to === id ? l.dataset.from : null;
                const og = other && nodeById(other);
                if (og) lines.push({ l: l, from: l.dataset.from === id, at: position(og), r: radius(og) });
            });
            state.node = { g: g, id: id, start: p, origin: position(g), r: radius(g), lines: lines, at: position(g) };
            return;
        }
        state.drag = { x: e.clientX, y: e.clientY, view: Object.assign({}, state.view), s: p.s };
    });
    on(window, 'pointermove', function (e) {
        const n = state.node;
        if (n) {
            const p = toGraph(e);
            if (!state.moved && Math.abs(p.x - n.start.x) + Math.abs(p.y - n.start.y) < 4 / p.s) return;
            if (!state.moved) { state.moved = true; svg.classList.add('moving'); }
            n.at = { x: n.origin.x + p.x - n.start.x, y: n.origin.y + p.y - n.start.y };
            n.g.setAttribute('transform', 'translate(' + n.at.x.toFixed(1) + ' ' + n.at.y.toFixed(1) + ')');
            n.lines.forEach(function (x) {
                if (x.from) placeLine(x.l, n.at, n.r, x.at, x.r); else placeLine(x.l, x.at, x.r, n.at, n.r);
            });
            return;
        }
        const d = state.drag;
        if (!d) return;
        const mx = e.clientX - d.x, my = e.clientY - d.y;
        if (!state.moved && Math.abs(mx) + Math.abs(my) < 4) return;
        if (!state.moved) { state.moved = true; svg.classList.add('panning'); }
        state.view = { x: d.view.x - mx / d.s, y: d.view.y - my / d.s, w: d.view.w, h: d.view.h };
        apply();
    });
    on(window, 'pointerup', function () {
        const n = state.node;
        if (n) {
            state.node = null;
            svg.classList.remove('moving');
            if (state.moved && state.target) state.target.invokeMethodAsync('OnNodeMoved', n.id, n.at.x, n.at.y);
            return;
        }
        if (!state.drag) return;
        state.drag = null;
        svg.classList.remove('panning');
    });
    // A pan or a move ends with a click on whatever is under the pointer; don't let it select a node.
    on(svg, 'click', function (e) {
        if (state.moved) { e.stopPropagation(); e.preventDefault(); state.moved = false; }
    }, true);

    function highlight(id) {
        svg.classList.toggle('hovering', !!id);
        svg.querySelectorAll('.hl').forEach(function (el) { el.classList.remove('hl'); });
        if (!id) return;
        const near = new Set([id]);
        svg.querySelectorAll('.kgb-gedge').forEach(function (l) {
            if (l.dataset.from === id || l.dataset.to === id) { l.classList.add('hl'); near.add(l.dataset.from); near.add(l.dataset.to); }
        });
        svg.querySelectorAll('.kgb-gnode').forEach(function (g) { if (near.has(g.dataset.id)) g.classList.add('hl'); });
    }
    on(svg, 'pointerover', function (e) {
        if (state.drag || state.node) return;
        const g = e.target.closest('.kgb-gnode');
        highlight(g ? g.dataset.id : null);
    });
    on(svg, 'pointerleave', function () { if (!state.node) highlight(null); });

    state.handle = {
        dispose: function () {
            off.forEach(function (f) { f(); });
            off.length = 0;
            if (svg.kgbGraph === state) delete svg.kgbGraph;
        }
    };
    return state.handle;
};
window.asb.fitGraph = function (svg) {
    if (!svg || !svg.kgbGraph) return;
    const p = svg.kgbGraph.home.split(/\s+/).map(Number);
    svg.kgbGraph.view = { x: p[0], y: p[1], w: p[2], h: p[3] };
    svg.kgbGraph.apply();
};
// Moves the view so (x, y) is in the middle, zooming in to at least the fitted size.
window.asb.centerGraph = function (svg, x, y) {
    if (!svg || !svg.kgbGraph) return;
    const st = svg.kgbGraph, p = st.home.split(/\s+/).map(Number);
    const w = Math.min(st.view.w, p[2]), h = st.view.h * (w / st.view.w);
    st.view = { x: x - w / 2, y: y - h / 2, w: w, h: h };
    st.apply();
};
// Graph page preview: page links open in the panel (OnPanelLink) instead of navigating away.
window.asb.bindGraphPanel = function (el, target) {
    if (!el) return;
    el.kgbTarget = target;
    if (el.dataset.kgbBound) return;
    el.dataset.kgbBound = '1';
    el.addEventListener('click', function (e) {
        const a = e.target.closest('a.wl, a.wu');
        if (!a || e.ctrlKey || e.metaKey || e.shiftKey || e.button !== 0) return;
        e.preventDefault();
        e.stopPropagation();
        el.kgbTarget.invokeMethodAsync('OnPanelLink', a.getAttribute('href'));
    });
};

// Chat composer: Enter sends, Shift+Enter adds a new line, Esc stops a stream.
window.asb.bindComposer = function (id, target) {
    const el = document.getElementById(id);
    if (!el || el.dataset.kgbBound) return;
    el.dataset.kgbBound = '1';
    el.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            target.invokeMethodAsync('OnComposerEnter', el.value);
        } else if (e.key === 'Escape') {
            target.invokeMethodAsync('OnComposerEscape');
        }
    });
};
window.asb.clearValue = function (id) {
    const el = document.getElementById(id);
    if (el) { el.value = ''; el.dispatchEvent(new Event('input', { bubbles: true })); }
};
