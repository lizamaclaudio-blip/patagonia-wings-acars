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

-- Ãndice para consulta rÃ¡pida
CREATE INDEX IF NOT EXISTS idx_acars_releases_active ON acars_releases (is_active, release_date DESC);

-- Acceso pÃºblico de lectura (sin auth) para que la app pueda verificar versiones
ALTER TABLE acars_releases ENABLE ROW LEVEL SECURITY;

CREATE POLICY "acars_releases_public_read"
    ON acars_releases FOR SELECT
    USING (true);

-- ============================================================
-- INSERTAR VERSIÃ“N 3.2.4
-- ============================================================
INSERT INTO acars_releases (version, download_url, notes, mandatory, is_active)
VALUES (
    '3.2.4',
    'https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/PatagoniaWingsACARSSetup-3.2.4.exe',
    'Novedades v3.2.4:

â€¢ Filtro real por rango y aeropuerto para Cadete
â€¢ Itinerarios alineados a aeronaves realmente autorizadas
â€¢ Reapertura estable del ACARS tras update
â€¢ ConfirmaciÃ³n visual al volver a abrir',
    false,
    true
);

-- ============================================================
-- CÃ“MO PUBLICAR PRÃ“XIMAS VERSIONES
-- Solo ejecutar este INSERT con la nueva versiÃ³n:
-- ============================================================
/*
-- Desactivar versiÃ³n anterior (opcional)
UPDATE acars_releases SET is_active = false WHERE version = '3.2.4';

-- Activar nueva versiÃ³n
INSERT INTO acars_releases (version, download_url, notes, mandatory, is_active)
VALUES (
    '2.0.12',
    'https://www.patagoniaw.com/downloads/PatagoniaWingsACARSSetup-2.0.12.exe',
    'Novedades v2.0.12: ...',
    false,
    true
);
*/

