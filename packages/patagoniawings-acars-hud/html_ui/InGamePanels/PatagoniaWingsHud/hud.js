(() => {
  'use strict';

  const PORT = 37677;
  const STATE_URL = `http://127.0.0.1:${PORT}/api/hud/state`;
  const POLL_MS = 1000;

  const LS_HIDDEN   = 'pw-hud-hidden';
  const LS_MODE     = 'pw-hud-mode';
  const LS_POSITION = 'pw-hud-position';

  const POSITIONS = ['pos-center', 'pos-left', 'pos-right'];
  const MODES     = ['mode-compact', 'mode-expanded'];

  // ── DOM refs ────────────────────────────────────────────────────────────────
  const body    = document.body;
  const hud     = document.getElementById('hud');
  const restore = document.getElementById('restore');
  const systems = document.getElementById('systems');
  const btnMode = document.getElementById('btnMode');
  const btnPos  = document.getElementById('btnPos');

  const els = {
    status:       document.getElementById('status'),
    flightNumber: document.getElementById('flightNumber'),
    dep:          document.getElementById('dep'),
    arr:          document.getElementById('arr'),
    aircraft:     document.getElementById('aircraft'),
    altitude:     document.getElementById('altitude'),
    gs:           document.getElementById('gs'),
    hdg:          document.getElementById('hdg'),
    vs:           document.getElementById('vs'),
    fuel:         document.getElementById('fuel'),
    qnh:          document.getElementById('qnh'),
    xpdr:         document.getElementById('xpdr'),
    pilotName:    document.getElementById('pilotName'),
    pilotRank:    document.getElementById('pilotRank'),
  };

  const SYSTEM_LABELS = [
    ['nav',         'NAV'],
    ['beaconStrobe','BCN/STB'],
    ['taxi',        'TAXI'],
    ['landing',     'LAND'],
    ['gear',        'GEAR'],
    ['apMaster',    'AP'],
    ['doors',       'DOORS'],
    ['parkingBrake','BRAKE'],
    ['xpdr',        'XPDR'],
  ];

  // ── Helpers ─────────────────────────────────────────────────────────────────
  function set(el, text, fallback = '--') {
    if (!el) return;
    el.textContent = (text == null || text === '') ? fallback : String(text);
  }

  function num(val, dec = 0) {
    const n = Number(val);
    return Number.isFinite(n) ? n.toLocaleString('en-US', { maximumFractionDigits: dec, minimumFractionDigits: 0 }) : null;
  }

  function lsGet(key, def) {
    try { const v = localStorage.getItem(key); return v !== null ? v : def; } catch { return def; }
  }
  function lsSet(key, val) {
    try { localStorage.setItem(key, val); } catch {}
  }

  // ── Rank badge ───────────────────────────────────────────────────────────────
  const RANK_ABBREV = [
    [/comandante\s+transatl[aá]ntico/i, 'CMD TLA'],
    [/comandante\s+primera/i,           'CMD 1RA'],
    [/comandante\s+internacional/i,     'CMD INT'],
    [/comandante\s+regional/i,          'CMD REG'],
    [/comandante\s+dom[eé]stico/i,      'CMD DOM'],
    [/comandante/i,                     'CMD'],
    [/primer oficial\s+transatl[aá]ntico/i, 'P/O TLA'],
    [/primer oficial\s+internacional/i, 'P/O INT'],
    [/primer oficial\s+regional/i,      'P/O REG'],
    [/primer oficial\s+dom[eé]stico/i,  'P/O DOM'],
    [/primer oficial/i,                 'P/O'],
    [/segundo oficial\s+dom[eé]stico/i, 'S/O DOM'],
    [/segundo oficial/i,                'S/O'],
    [/aspirante/i,                      'ASP'],
  ];

  function rankAbbrev(name) {
    if (!name) return '';
    for (const [rx, abbr] of RANK_ABBREV) {
      if (rx.test(name)) return abbr;
    }
    return name.length > 12 ? name.slice(0, 11) + '…' : name;
  }

  function rankClass(name, code) {
    const t = ((name || '') + ' ' + (code || '')).toLowerCase();
    if (!t.trim()) return 'rk-gray';
    if (t.includes('tla') || t.includes('transatl')) return 'rk-cyan';
    if (t.includes('primera') || t.includes('1ra'))  return 'rk-gold';
    if (t.includes('comandante') || t.includes('cmd')) return 'rk-amber';
    if (t.includes('primer') || t.includes('p/o'))   return 'rk-blue';
    if (t.includes('segundo') || t.includes('s/o'))  return 'rk-indigo';
    if (t.includes('asp'))                            return 'rk-gray';
    return 'rk-gray';
  }

  // ── Render ──────────────────────────────────────────────────────────────────
  function renderSystems(raw) {
    systems.innerHTML = '';
    const src = (raw && typeof raw === 'object') ? raw : {};
    for (const [key, label] of SYSTEM_LABELS) {
      const state = String(src[key] ?? 'na').toLowerCase();
      if (state === 'unsupported') continue;
      const pill = document.createElement('div');
      pill.className = `pill ${state}`;
      pill.textContent = label;
      pill.title = `${label}: ${state.toUpperCase()}`;
      systems.appendChild(pill);
    }
  }

  function render(data) {
    const online = !!(data && data.connected);
    hud.classList.toggle('hud--offline', !online);

    set(els.status, online ? `▸ ACARS LIVE · ${data.phase || ''}` : 'ACARS offline');
    set(els.flightNumber, data?.flightNumber || data?.callsign);
    set(els.dep,      data?.dep,  '----');
    set(els.arr,      data?.arr,  '----');
    set(els.aircraft, data?.aircraftType || data?.aircraftDisplayName || data?.aircraft);

    set(els.altitude, num(data?.altitudeFt) ?? '0');
    set(els.gs,       num(data?.groundSpeedKt) ?? '0');
    set(els.hdg,      (num(data?.headingDeg) ?? '000') + '°');
    set(els.vs,       num(data?.verticalSpeedFpm) ?? '0');

    const fuelKg  = num(data?.fuelCurrentKg);
    const fuelCap = Number(data?.fuelCapacityKg) > 10 ? num(data?.fuelCapacityKg) : null;
    set(els.fuel, fuelCap ? `${fuelKg ?? 0} / ${fuelCap}` : `${fuelKg ?? '—'}`);

    set(els.qnh,  data?.qnh ? String(data.qnh) : '—');
    const xMode = data?.xpdrMode ? ` ${String(data.xpdrMode).toUpperCase()}` : '';
    set(els.xpdr, `${data?.xpdrCode || '----'}${xMode}`);

    // Pilot name + rank
    const pName = (data?.pilotName || data?.callsign || '').trim();
    const pRankName = data?.pilotRankName || '';
    const pRankCode = data?.pilotRankCode || '';
    set(els.pilotName, pName || '—');

    const abbr = rankAbbrev(pRankName || pRankCode);
    set(els.pilotRank, abbr || '—');
    if (els.pilotRank) {
      els.pilotRank.className = `pilot-rank ${rankClass(pRankName, pRankCode)}`;
    }

    renderSystems(data?.systems);
  }

  // ── Poll ─────────────────────────────────────────────────────────────────────
  async function tick() {
    try {
      const res = await fetch(`${STATE_URL}?t=${Date.now()}`, { cache: 'no-store' });
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      render(await res.json());
    } catch {
      render(null);
    } finally {
      window.setTimeout(tick, POLL_MS);
    }
  }

  // ── State: show / hide / mode / position ─────────────────────────────────────
  function applyHidden(hidden) {
    body.classList.toggle('hud-hidden', hidden);
    lsSet(LS_HIDDEN, hidden ? '1' : '0');
  }

  function applyMode(mode) {
    body.classList.remove(...MODES);
    body.classList.add(mode);
    if (btnMode) btnMode.title = mode === 'mode-compact' ? 'Expandir HUD' : 'Compactar HUD';
    lsSet(LS_MODE, mode);
  }

  function applyPosition(pos) {
    body.classList.remove(...POSITIONS);
    body.classList.add(pos);
    lsSet(LS_POSITION, pos);
  }

  // ── Public API (attached to window for onclick handlers) ─────────────────────
  window.pwHud = {
    show() {
      applyHidden(false);
    },
    hide() {
      applyHidden(true);
    },
    toggleMode() {
      const current = body.classList.contains('mode-expanded') ? 'mode-expanded' : 'mode-compact';
      applyMode(current === 'mode-compact' ? 'mode-expanded' : 'mode-compact');
    },
    cyclePosition() {
      const current = POSITIONS.find(p => body.classList.contains(p)) || POSITIONS[0];
      const next = POSITIONS[(POSITIONS.indexOf(current) + 1) % POSITIONS.length];
      applyPosition(next);
    },
  };

  // ── Init ─────────────────────────────────────────────────────────────────────
  applyHidden(lsGet(LS_HIDDEN, '0') === '1');
  applyMode(lsGet(LS_MODE, 'mode-compact'));
  applyPosition(lsGet(LS_POSITION, 'pos-center'));

  render(null);
  tick();
})();
