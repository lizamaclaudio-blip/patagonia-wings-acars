-- Actualizar URL de descarga activa del ACARS 3.2.4
UPDATE acars_releases
SET download_url = 'https://qoradagitvccyabfkgkw.supabase.co/storage/v1/object/public/acars-releases/PatagoniaWingsACARSSetup-3.2.4.exe'
WHERE version = '3.2.4';
