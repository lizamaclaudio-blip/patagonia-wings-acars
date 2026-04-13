-- ============================================================
-- TABLA: acars_releases
-- Gestiona las versiones del ACARS Patagonia Wings
-- Ejecutar en Supabase SQL Editor
-- ============================================================

CREATE TABLE IF NOT EXISTS acars_releases (
    id            uuid DEFAULT gen_random_uuid() PRIMARY KEY,
    version       text NOT NULL,
    download_url  text NOT NULL,
    notes         text,
    mandatory     boolean DEFAULT false,
    is_active     boolean DEFAULT true,
    release_date  timestamptz DEFAULT now(),
    created_at    timestamptz DEFAULT now()
);

-- Índice para consulta rápida
CREATE INDEX IF NOT EXISTS idx_acars_releases_active ON acars_releases (is_active, release_date DESC);

-- Acceso público de lectura (sin auth) para que la app pueda verificar versiones
ALTER TABLE acars_releases ENABLE ROW LEVEL SECURITY;

CREATE POLICY "acars_releases_public_read"
    ON acars_releases FOR SELECT
    USING (true);

-- ============================================================
-- INSERTAR VERSIÓN 2.0.11
-- ============================================================
INSERT INTO acars_releases (version, download_url, notes, mandatory, is_active)
VALUES (
    '2.0.11',
    'https://www.patagoniaw.com/downloads/PatagoniaWingsACARSSetup-2.0.11.exe',
    'Novedades v2.0.11:

• Soporte completo Airbus A319 Headwind
• Integración MobiFlight WASM Module (lectura de LVARs)
• Lectura correcta de luces: Beacon, Strobe, Landing, Nav, Taxi
• Lectura N1 de motores
• Fallback automático FSUIPC → SimConnect
• Detección automática del tipo de avión
• Mejoras en UI de login
• Correcciones en telemetría (squawk, cabin altitude)',
    false,
    true
);

-- ============================================================
-- CÓMO PUBLICAR PRÓXIMAS VERSIONES
-- Solo ejecutar este INSERT con la nueva versión:
-- ============================================================
/*
-- Desactivar versión anterior (opcional)
UPDATE acars_releases SET is_active = false WHERE version = '2.0.11';

-- Activar nueva versión
INSERT INTO acars_releases (version, download_url, notes, mandatory, is_active)
VALUES (
    '2.0.12',
    'https://www.patagoniaw.com/downloads/PatagoniaWingsACARSSetup-2.0.12.exe',
    'Novedades v2.0.12: ...',
    false,
    true
);
*/
