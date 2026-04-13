-- Actualizar URL de descarga a GitHub Releases
UPDATE acars_releases
SET download_url = 'https://github.com/lizamaclaudio-blip/patagonia-wings-acars/releases/latest/download/PatagoniaWings.Acars.Master.exe'
WHERE version = '2.0.11';
