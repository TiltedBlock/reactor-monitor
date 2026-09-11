# REACTOR

A live hardware telemetry console for Windows desktops. It reads sensors
directly through LibreHardwareMonitor and presents a curated set of ~20 values
as a single control board rather than a sensor dump.

Built with .NET 8 and Avalonia. Windows only by design.

---

## Running it

```bash
dotnet run --project src/Reactor.App -c Release
```

Or run the built executable directly:

```bash
src/Reactor.App/bin/Release/net8.0/Reactor.exe
```

### CPU package telemetry needs two things

**1. Administrator rights.** NVMe drive temperatures need them too. The
**LIMITED TELEMETRY** band offers a `RESTART ELEVATED` button that relaunches
through the normal Windows consent prompt.

**2. The PawnIO kernel driver.** Reading package temperature, package power and
per-core clocks means reading model-specific registers and the on-die
management controller. LibreHardwareMonitor 0.9.x no longer ships its own
driver: it carries PawnIO modules (`AMDFamily17`, `RyzenSMU`, `IntelMSR`,
`LpcIO`) as embedded resources and executes them through the separately
installed, Microsoft-signed [PawnIO](https://pawnio.eu) driver. The older
WinRing0 driver it used to bundle is now blocked by Windows' vulnerable-driver
blocklist.

Without PawnIO those sensors are still published, they simply never return a
value — `Package` power reads a flat `0`, `Core (Tctl/Tdie)` reads `0`, and the
nominal per-core clocks read `null`. REACTOR treats that as absence rather than
measurement (see `MetricPlausibility`) and the banner names the actual blocker
instead of blaming elevation.

Everything else — CPU load and per-core load, the whole GPU module, memory,
network — uses ordinary Windows and vendor APIs and works without either.

### Keys and flags

| | |
|---|---|
| `F12` | toggle the raw sensor diagnostics view |
| `F11` | maximise / restore |
| `Esc` | close any open overlay |
| `--diagnostics` | start with the diagnostics view open |
| `--screenshot <path> [seconds]` | render the window to a PNG and exit (dev aid) |

## Configuration

`%APPDATA%\Reactor\config.json`, created on first run. The `CFG` button edits
the three settings worth changing from the UI; everything else — the status
thresholds and the wall-power model — is edited in the file.

```jsonc
{
  "PollIntervalMs": 750,
  "ElectricityPricePerKWh": 0.32,
  "CurrencySymbol": "€",
  "TemperatureUnit": "Celsius",
  "HistorySeconds": 120,
  "WallPower": {
    "BaselineWatts": 45,        // board, RAM, drives, fans, peripherals
    "PsuEfficiency": 0.90,
    "AssumedCpuWattsWhenUnknown": 45,
    "AssumedGpuWattsWhenUnknown": 30
  },
  "Thresholds": {
    "CpuTempHighC": 80, "CpuTempWarnC": 90,
    "GpuTempHighC": 75, "GpuTempWarnC": 85,
    "GpuHotspotHighC": 90, "GpuHotspotWarnC": 100,
    "CpuPowerCeilingWatts": 100, "GpuPowerCeilingWatts": 320
  }
}
```

Logs go to `%APPDATA%\Reactor\reactor.log`, including the full sensor mapping
decided at startup and any sensor that failed to read.

## Project layout

```
src/Reactor.Core        pure logic - no hardware, no UI, fully testable
  Sensors/                sensor vocabulary, rule table, mapper, plausibility
  Telemetry/              rolling history, session stats, derived metrics, status
  Configuration/          config model + JSON store
  Presentation/           number formatting
src/Reactor.Hardware     LibreHardwareMonitor integration + the polling thread
src/Reactor.App          Avalonia UI: custom controls, view models, views
tools/Reactor.Probe      headless sensor dump for debugging a machine
tests/Reactor.Core.Tests unit tests for the logic layer
```

The dependency direction is one-way: `App → Hardware → Core`. `LibreHardwareSource`
is the only file in the solution that references the monitoring library.

## How sensor mapping works

Hardware libraries name things inconsistently between vendors, chip generations
and driver versions. Nothing above the mapping layer is allowed to know those
names.

1. **Discovery** — every sensor becomes a neutral `SensorDescriptor`
   (identifier, name, kind, hardware kind, hardware name).
2. **Device selection** — `GpuSelector` and `StorageSelector` each pick one
   device and discard the others' sensors before mapping, so no rule can
   aggregate across devices. Desktop Ryzen chips carry an integrated Radeon that
   reports its own load, power and a 512 MB "VRAM", and a machine with several
   NVMe drives exposes several composite temperatures; enumeration order does
   not reliably put the interesting device first.
3. **Rule matching** — `SensorRules` lists, per metric, an ordered set of
   candidate name patterns. An earlier pattern always beats a later one, and an
   aggregating rule (`Max`, `Sum`, `Average`) only ever combines sensors matched
   by the *same* pattern.
4. **Role** — `SensorSemantics` separates live readings from device-reported
   limits. A drive publishes `Warning Temperature` and `Critical Temperature`
   alongside `Composite Temperature`; those are firmware constants, and a rule
   that matched "Temperature" loosely once reported a drive as sitting at its
   own 88 °C critical point. Threshold sensors are excluded from every
   telemetry rule by name, generically, and captured separately in
   `MetricsSnapshot.Limits` where the status evaluator can use them.
5. **Plausibility** — `MetricPlausibility` rejects structurally impossible
   readings. This is what keeps `0.0 °C` off the screen when the CPU driver
   cannot be loaded: the sensors exist and report a flat zero, which is absence,
   not a measurement.
6. **Unresolved metrics** are logged, counted in the status bar, and listed in
   the diagnostics view. The UI shows `—` and hides secondary values rather than
   failing.

Adding support for a new naming scheme means adding one pattern to
`SensorRules` — no changes anywhere else.

## Derived values

Everything below is modelled, not measured, and is labelled `EST` in the UI.

- **System draw** — `(CPU + GPU package watts + baseline) / PSU efficiency`.
  If a component reports no power sensor, a configured assumption is substituted.
- **Heat output** — the same number in BTU/h. Essentially all electrical input
  to a PC leaves as heat.
- **Session energy** — the wall-power estimate integrated over time with the
  trapezoid rule. Gaps longer than 5 s are clamped, so suspending the machine
  cannot invent kilowatt-hours.
- **Cost** — session energy and instantaneous draw against the configured tariff.

## Status classification

`StatusEvaluator` is the only thing that decides what the banner says, and every
number it uses comes from `StatusThresholds`. Escalation order:

`NOMINAL → ACTIVE → HIGH LOAD → POWER LIMITED → THERMAL LOAD → WARNING`

The highest applicable state wins and the banner shows which reading caused it.
Device-reported limits beat configured guesses wherever a device publishes them:
an NVIDIA card's own power-limit headroom drives `POWER LIMITED`, and a drive's
own warning and critical temperatures drive its thermal states.

Only live, plausible readings can escalate the banner. A metric that is
unavailable, or a firmware limit, can never raise a warning.

This is informational, not a safety system.

## Debugging a machine

```bash
dotnet run --project tools/Reactor.Probe               # identity, driver state, bindings
dotnet run --project tools/Reactor.Probe -- --all      # plus every raw sensor
dotnet run --project tools/Reactor.Probe -- --raw cpu  # raw sensors, filtered by device
```

The probe reports privilege and kernel-driver state, names every sensor behind
an aggregate binding, flags bindings that had more than one candidate, and
prints raw values distinguishing `<null>` (the library returned nothing) from
`0` (it genuinely reported zero). That difference is what identifies a driver
problem as opposed to a mapping problem.

The in-app diagnostics view (`F12`) shows the same information: a canonical
metric mapping table, every device and raw sensor with its identifier, limits
tagged `LIMIT`, and a banner listing any metric that had several candidates.

## Tests

```bash
dotnet test
```

Covers sensor selection and aggregation, GPU disambiguation, the plausibility
filter, the rolling buffer, peak tracking, energy integration, derived metrics,
status classification and number formatting. Layout is not unit-tested.
