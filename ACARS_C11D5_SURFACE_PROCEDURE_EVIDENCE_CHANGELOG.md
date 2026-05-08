# ACARS C11D5 - Surface Procedure Evidence (ACARS recorder only)

Base: local C10/C11 stack through C11D4C.

Files touched:
- PatagoniaWings.Acars.Core/Models/SimData.cs
- PatagoniaWings.Acars.Core/Services/FlightService.cs
- PatagoniaWings.Acars.Core/Services/PirepXmlBuilder.cs
- PatagoniaWings.Acars.Master/ViewModels/InFlightViewModel.cs
- PatagoniaWings.Acars.Master/Views/Pages/InFlightPage.xaml

Purpose:
- Start recording procedure evidence now that C11C runway geometry and C11D taxi/gate payload are available.
- Classify the current operational surface phase as evidence only: gate origin, taxi out, runway departure, approach/final, runway arrival, taxi in, gate arrival, airborne or intermediate ground.
- Record expected lights/XPDR evidence for that phase without calculating score or penalties in ACARS.
- Expose a compact UI line: C11D5 reglaje evidencia.
- Add raw XML fields so Web/Supabase can later implement the official reglaje.

Explicit non-goals:
- No score changes.
- No wallet/economy changes.
- No Web/Supabase changes.
- No release/version bump.
- No publish/autoupdate.

Validation required:
1. MSBuild Release x64 must finish with 0 errors.
2. In MSFS at SCTB/SCEL, ACARS should show a C11D5 line with OK or EVIDENCE_REVIEW.
3. Any flags shown are evidence for later Web scoring, not ACARS penalties.
