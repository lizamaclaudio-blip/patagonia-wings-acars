# ACARS C19A - WPF Binding Red Warnings Cleanup

Base local: ACARS 7.0.17 + cambios C10-C18 locales sin publicar.

Objetivo:
- Eliminar avisos rojos WPF de binding por `RelativeSource AncestorType=Window` cuando las páginas aún no están montadas en una ventana.
- No tocar lógica de vuelo, scoring, Facilities, XML, Web, Supabase, economía ni versionado.

Archivos modificados:
- `PatagoniaWings.Acars.Master/Views/Pages/InFlightPage.xaml`
  - Se elimina `DataContext` por RelativeSource Window en `PageRoot`.
  - Header de modo/vuelo/matrícula ahora usa propiedades directas del `InFlightViewModel`.
- `PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs`
  - Agrega `FlightNumber`, `AircraftRegistrationDisplay`, `FlightModeDisplayLabel`, `IsOnlineFlightMode` y helper `FirstNonEmpty`.
  - Notifica esas propiedades al refrescar snapshot de ruta/despacho.
- `PatagoniaWings.Acars.Master/Views/Pages/PreFlightPage.xaml`
  - Se elimina `DataContext` por RelativeSource Window en `PageRoot`; el code-behind ya resuelve `PreFlightViewModel`.
- `PatagoniaWings.Acars.Master/Views/Pages/PostFlightPage.xaml`
  - Se elimina `DataContext` por RelativeSource Window en `PageRoot`.
  - Matrícula se enlaza a `AircraftRegistrationDisplay` del `PostFlightViewModel`.
  - Botón cancelar usa evento local `CancelButton_Click` en vez de binding a Window.
- `PatagoniaWings.Acars.Master/Views/Pages/PostFlightPage.xaml.cs`
  - Agrega `CancelButton_Click` para ejecutar `GoLiveFlightCommand` desde el shell si está disponible.
- `PatagoniaWings.Acars.Master/ViewModels/PostFlightViewModel.cs`
  - Agrega `AircraftRegistrationDisplay` desde dispatch activo o reporte.
- `PatagoniaWings.Acars.Master/Views/Pages/SupportPage.xaml`
  - Se elimina `DataContext="{Binding SupportVM}"`; el code-behind ya asigna `SupportVM` desde el shell.

Validación local realizada:
- XML parse OK para las páginas XAML incluidas.
- Scan sin `RelativeSource AncestorType=Window`, `DataContext.MainVM` ni `Binding SupportVM` en las páginas parchadas.

Pendiente usuario:
- Copiar parche sobre repo ACARS.
- Compilar Release x64.
- Abrir ACARS y revisar salida WPF: los avisos rojos de ancestor Window/SupportVM deberían desaparecer.
