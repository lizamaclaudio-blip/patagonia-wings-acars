# ACARS C8 Build Fix — Origin/Destination property names

## Motivo
El build Release fallaba en `PirepXmlBuilder.cs` porque C8 usó propiedades inexistentes:

- `PreparedDispatch.OriginIcao`
- `PreparedDispatch.DestinationIcao`
- `FlightReport.OriginIcao`
- `FlightReport.DestinationIcao`

La base real usa:

- `PreparedDispatch.DepartureIcao`
- `PreparedDispatch.ArrivalIcao`
- `FlightReport.DepartureIcao`
- `FlightReport.ArrivalIcao`

## Cambio aplicado
Se corrige `<PhaseTestRunManifest>` para escribir:

- `<Origin>` desde `dispatch.DepartureIcao` / `report.DepartureIcao`
- `<Destination>` desde `dispatch.ArrivalIcao` / `report.ArrivalIcao`

## Alcance
Solo corrige compilación C8. No toca Web, Supabase, scoring, economía, HUD, SayIntentions, SimBrief ni Route Finder.
