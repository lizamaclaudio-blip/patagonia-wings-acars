# ACARS C10 — Runway / Taxiway / TDZ Alignment Detector

Base: `2d04f43 release: publish acars 7.0.17 c9 gate close surface audit`.

## Archivos tocados

- `PatagoniaWings.Acars.Core/Models/SimData.cs`
- `PatagoniaWings.Acars.Core/Services/FlightService.cs`
- `PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs`
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
- `PatagoniaWings.Acars.Master/Views/Pages/InFlightPage.xaml`

## Cambios

- Agrega evidencia C10 para pista/rodaje/TDZ/alineamiento sin calcular score oficial en ACARS.
- Detecta candidatos operacionales: entrada a pista, alineamiento, takeoff roll, TDZ candidato, landing roll, salida de pista y taxiway probable.
- Estima identificador de pista por rumbo (`EstimatedRunwayIdent`) y diferencia de heading (`RunwayHeadingDeltaDeg`).
- Expone línea visual `C10 pista/TDZ` en pantalla de vuelo.
- Agrega `<RunwayTdzAuditReport>` al XML PIREP.
- Enriquecimiento de `EventTimeline`, `KeyInstants` e instantes XML con campos C10.

## Limitación intencional

C10 no afirma nombres exactos de pista/taxiway ni TDZ real por distancia desde umbral porque aún no hay geometría aeroportuaria/navdata cargada. Marca `RunwayGeometryAvailable=false` y deja todo como evidencia raw para Web/Supabase.

## No toca

- Web/Supabase scoring oficial
- Economía, wallet, salary, ledger
- HUD/SayIntentions/SimBrief
