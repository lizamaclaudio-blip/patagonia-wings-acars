(() => {
  'use strict';

  const PORT = 37677;
  const STATE_URL = `http://127.0.0.1:${PORT}/api/hud/state`;
  const HEALTH_URL = `http://127.0.0.1:${PORT}/api/hud/health`;
  const POLL_MS = 1000;

  const LS_HIDDEN = 'pw-hud-hidden';
  const LS_MODE = 'pw-hud-mode';
  const MODES = ['mode-compact', 'mode-expanded'];

  const body = document.body;
  const hud = document.getElementById('hud');
  const systems = document.getElementById('systems');
  const btnMode = document.getElementById('btnMode');

  const els = {
    status: document.getElementById('status'),
    flightNumber: document.getElementById('flightNumber'),
    dep: document.getElementById('dep'),
    arr: document.getElementById('arr'),
    aircraft: document.getElementById('aircraft'),
    altitude: document.getElementById('altitude'),
    gs: document.getElementById('gs'),
    hdg: document.getElementById('hdg'),
    vs: document.getElementById('vs'),
    fuel: document.getElementById('fuel'),
    qnh: document.getElementById('qnh'),
    xpdr: document.getElementById('xpdr'),
    pilotName: document.getElementById('pilotName'),
    pilotRank: document.getElementById('pilotRank'),
  };

  const SYSTEM_LABELS = [
    ['nav', 'NAV'],
    ['beaconStrobe', 'BCN/STB'],
    ['taxi', 'TAXI'],
    ['landing', 'LAND'],
    ['gear', 'GEAR'],
    ['apMaster', 'AP'],
    ['doors', 'DOORS'],
    ['parkingBrake', 'BRAKE'],
    ['xpdr', 'XPDR'],
  ];

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

  function render(data, bridgeOnline = false) {
    const simConnected = !!(data && data.connected);
    const hasTelemetry = !!(data && (data.flightActive || Number(data.altitudeFt) > 0 || Number(data.groundSpeedKt) > 0 || data.flightNumber));
    const online = simConnected || hasTelemetry;

    hud.classList.toggle('hud--offline', !online);
    if (online) {
      applyHidden(false);
    }

    let status = 'ACARS OFFLINE';
    if (online) status = `ACARS LIVE${data?.phase ? ' | ' + data.phase : ''}`;
    else if (bridgeOnline) status = 'BRIDGE OK | ESPERANDO SIM';
    set(els.status, status);

    set(els.flightNumber, data?.flightNumber || data?.callsign);
    set(els.dep, data?.dep, '----');
    set(els.arr, data?.arr, '----');
    set(els.aircraft, data?.aircraftDisplayName || data?.aircraftType || data?.aircraft);

    set(els.altitude, num(data?.altitudeFt) ?? '0');
    set(els.gs, num(data?.groundSpeedKt) ?? '0');
    set(els.hdg, `${num(data?.headingDeg) ?? '000'}°`);
    set(els.vs, num(data?.verticalSpeedFpm) ?? '0');

    const fuelKg = num(data?.fuelCurrentKg);
    const fuelCap = Number(data?.fuelCapacityKg) > 10 ? num(data?.fuelCapacityKg) : null;
    set(els.fuel, `${fuelKg ?? '--'} / ${fuelCap ?? 'N/D'}`);

    set(els.qnh, data?.qnh ? String(data.qnh) : '--');
    const xMode = data?.xpdrMode ? ` ${String(data.xpdrMode).toUpperCase()}` : '';
    set(els.xpdr, `${data?.xpdrCode || '----'}${xMode}`);

    set(els.pilotName, (data?.pilotName || data?.callsign || '').trim() || '--');
    set(els.pilotRank, (data?.pilotRankCode || data?.pilotRankName || '--'));

    renderSystems(data?.systems);
  }

  async function tick() {
    try {
      const [healthRes, stateRes] = await Promise.all([
        fetch(`${HEALTH_URL}?t=${Date.now()}`, { cache: 'no-store' }),
        fetch(`${STATE_URL}?t=${Date.now()}`, { cache: 'no-store' }),
      ]);

      const bridgeOnline = healthRes.ok;
      if (!stateRes.ok) {
        render(null, bridgeOnline);
      } else {
        render(await stateRes.json(), bridgeOnline);
      }
    } catch {
      render(null, false);
    } finally {
      window.setTimeout(tick, POLL_MS);
    }
  }

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

  window.pwHud = {
    show() { applyHidden(false); },
    hide() { applyHidden(true); },
    toggleMode() {
      const current = body.classList.contains('mode-expanded') ? 'mode-expanded' : 'mode-compact';
      applyMode(current === 'mode-compact' ? 'mode-expanded' : 'mode-compact');
    },
    cyclePosition() {}
  };

  applyHidden(lsGet(LS_HIDDEN, '0') === '1');
  applyMode(lsGet(LS_MODE, 'mode-compact'));

  render(null, false);
  tick();
})();
