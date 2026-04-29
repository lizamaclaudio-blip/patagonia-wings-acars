(() => {
  const DEFAULT_PORT = 37677;
  const stateUrl = `http://127.0.0.1:${DEFAULT_PORT}/api/hud/state`;
  const pollMs = 1000;

  const $ = (id) => document.getElementById(id);
  const hud = $("hud");
  const systems = $("systems");

  const fields = {
    connection: $("connection"),
    flightNumber: $("flightNumber"),
    dep: $("dep"),
    arr: $("arr"),
    aircraft: $("aircraft"),
    altitude: $("altitude"),
    gs: $("gs"),
    hdg: $("hdg"),
    vs: $("vs"),
    fuel: $("fuel"),
    qnh: $("qnh"),
    xpdr: $("xpdr"),
  };

  const systemLabels = [
    ["nav", "NAV"],
    ["beaconStrobe", "BCN/STB"],
    ["taxi", "TAXI"],
    ["landing", "LAND"],
    ["gear", "GEAR"],
    ["apMaster", "AP"],
    ["doors", "DOORS"],
    ["parkingBrake", "BRAKE"],
    ["xpdr", "XPDR"],
  ];

  function setText(el, value, fallback = "--") {
    if (!el) return;
    const text = value === undefined || value === null || value === "" ? fallback : String(value);
    el.textContent = text;
  }

  function num(value, digits = 0) {
    const n = Number(value);
    if (!Number.isFinite(n)) return null;
    return n.toLocaleString("en-US", { maximumFractionDigits: digits, minimumFractionDigits: 0 });
  }

  function renderSystems(raw) {
    systems.innerHTML = "";
    const source = raw && typeof raw === "object" ? raw : {};
    for (const [key, label] of systemLabels) {
      const state = String(source[key] ?? "na").toLowerCase();
      if (state === "unsupported") continue;
      const pill = document.createElement("div");
      pill.className = `pill ${state}`;
      pill.textContent = label;
      pill.title = `${label}: ${state.toUpperCase()}`;
      systems.appendChild(pill);
    }
  }

  function render(data) {
    hud.classList.toggle("hud--offline", !data || !data.connected);
    setText(fields.connection, data?.connected ? "ACARS LIVE" : "ACARS OFFLINE");
    setText(fields.flightNumber, data?.flightNumber || data?.callsign);
    setText(fields.dep, data?.dep, "----");
    setText(fields.arr, data?.arr, "----");
    setText(fields.aircraft, data?.aircraft || data?.aircraftType);
    setText(fields.altitude, `${num(data?.altitudeFt) ?? 0} FT`);
    setText(fields.gs, `${num(data?.groundSpeedKt) ?? 0} KT`);
    setText(fields.hdg, `${num(data?.headingDeg) ?? "000"}°`);
    setText(fields.vs, `${num(data?.verticalSpeedFpm) ?? 0} FPM`);

    const fuelCurrent = num(data?.fuelCurrentKg);
    const fuelCapacity = Number(data?.fuelCapacityKg) > 0 ? num(data?.fuelCapacityKg) : "N/D";
    setText(fields.fuel, `${fuelCurrent ?? 0} / ${fuelCapacity} KG`);

    setText(fields.qnh, data?.qnh ? String(data.qnh) : "N/D");
    const xpdrMode = data?.xpdrMode ? ` ${String(data.xpdrMode).toUpperCase()}` : "";
    setText(fields.xpdr, `${data?.xpdrCode || data?.xpdr || "----"}${xpdrMode}`);

    renderSystems(data?.systems);
  }

  async function tick() {
    try {
      const response = await fetch(`${stateUrl}?t=${Date.now()}`, { cache: "no-store" });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      render(await response.json());
    } catch (error) {
      render(null);
    } finally {
      window.setTimeout(tick, pollMs);
    }
  }

  render(null);
  tick();
})();
