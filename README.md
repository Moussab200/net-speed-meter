<div align="center">

# ⚡ Net Speed Meter

**A featherweight internet speed widget that lives on your taskbar like it belongs there**

[![Platform](https://img.shields.io/badge/platform-Windows-0078D6?logo=windows&logoColor=white)](#)
[![Language](https://img.shields.io/badge/C%23-.NET_Framework-239120?logo=csharp&logoColor=white)](#)
[![Size](https://img.shields.io/badge/size-~80KB-success)](#)
[![RAM](https://img.shields.io/badge/RAM-~30MB-success)](#)
[![Dependencies](https://img.shields.io/badge/dependencies-zero-success)](#)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)

### [⬇️ Download the latest release](https://github.com/Moussab200/net-speed-meter/releases/latest)

</div>

---

A tiny widget that shows your live internet speed on screen, tracks cumulative data usage, and includes a built-in speed test.

**Why this one's different:**

- 🪶 **~80KB, ~30MB RAM** — no Electron, no framework, zero external dependencies
- 🎨 **Automatically matches your taskbar's color and height** — reads like a native part of it, not a floating overlay
- 🔒 **Built with security as a foundation, not an afterthought** — the running copy lives in a protected `Program Files` folder, is digitally signed, and is hardened against file tampering (full details below)
- ✅ **43 automated tests + a quality gate** — run against the real app code, not a copy of it
- 🌙 **No hidden network calls** — the only connection it ever makes is the one you trigger yourself with the speed test button

Fully local — **it never uploads or transmits anything.**

---

## Running it

The live copy lives in `C:\Program Files\Net Speed Meter\`, with shortcuts on the Desktop and in the Start Menu. Double-click and that's it — no setup dialog on first launch, the widget just appears and gets out of your way.

`D:\New\net-speed-meter\` (this repo) is the **development** folder — source and build scripts, not where the app actually runs from.

**All settings live in the right-click menu on the widget itself.**

There's also a tray icon (under the hidden-icons chevron next to the clock) — use it to hide the widget or quit the app, and hover it for a tooltip with the current speed and today's usage.

---

## Security

The app's own attack surface is tiny: no listening port, no inbound connections, no shelling out to other programs, no dynamically loaded libraries, and no admin rights required to run. The only network call it ever makes is the one you trigger by clicking the speed test button.

The realistic risk isn't "someone hacks the running program" — it's **someone replacing the exe** with a malicious one, especially since it's wired into Windows autostart.

**The defense is prevention, not detection:**

| Measure | Why it helps |
|---|---|
| Live copy in `Program Files` | Writing there requires admin — malware running as your regular user **can't overwrite the file** |
| Desktop shortcut instead of a second exe | No writable copy sitting in a folder synced by OneDrive |
| Digital signature | Verify it yourself anytime: right-click → Properties → Digital Signatures |
| Hardened DLL search path | Nothing can plant a malicious DLL in the app's folder and have it picked up |

**Check integrity anytime:**

```
powershell -ExecutionPolicy Bypass -File verify.ps1
```

Checks the signature, the file hash, folder permissions, and every autostart/shortcut path. **Deliberately a separate script, not a self-check inside the app** — a self-check runs inside the same code it's auditing, so it can't be trusted against a deliberate attacker, and it would only add runtime overhead for no real benefit.

**Publish a new build after any change** (asks for admin once):

```
powershell -ExecutionPolicy Bypass -File deploy.ps1
```

Builds, signs, installs into the protected folder, and updates every shortcut.

### Why there's no auto-update

Deliberate. Auto-update sounds like a security feature, but it's the opposite — it's a ready-made channel for pushing code onto your machine later. The app is safer and lighter without one.

### A known trap: antivirus false positives

Malwarebytes (and others) may quarantine this app even though it's clean. The combination of traits matches behavioral-detection patterns: an exe not signed by a well-known publisher + a window hidden from the taskbar + a Run-key autostart entry + a silent network download (the speed test) + a brand-new file nobody's seen before. Every one of those traits exists for a legitimate, explicit reason — but together they look suspicious to an automated scanner.

Fix: add an exclusion for `C:\Program Files\Net Speed Meter` in your antivirus.

## Preventing regressions

Every category of bug that actually happened in this project now has an automated check that keeps it from coming back. **`deploy.ps1` refuses to publish if any check fails.**

```
powershell -ExecutionPolicy Bypass -File check-all.ps1
```

What it checks:

| Check | Bug it prevents |
|---|---|
| **Contrast** (5,832 background colors) | "white text on a white background" — the widget disappearing under a light theme |
| File encoding (BOM) | Non-ASCII text turning into garbage and breaking the build |
| Warning-free build | Silent errors |
| PowerShell script syntax | A broken script that only fails at runtime |
| Unit formatting | Wrong KB/MB/GB or byte↔bit conversion |
| Usage rollover | Counters lost when the day/month changes |
| Counter deltas | A bogus huge number when the network adapter resets |
| Settings round-trip | A setting that saves but never loads back = silently lost |
| Security checks | New network endpoints, spawning processes, cert bypass, hidden data |

The logic tests on their own (43 checks, run against the **real app functions**, not a copy):

```
run-tests.cmd
```

## Building from source

```
build.cmd
```

Uses the C# compiler that ships with Windows — **no SDK, no Node, no Python, no internet required**. Produces a single `NetSpeedMeter.exe`, around 80KB.

To regenerate the icon (after changing the default colors in the code):

```
powershell -ExecutionPolicy Bypass -File make-icon.ps1
```

### Why C# instead of Electron

This app runs 24/7, so being lightweight was a hard requirement:

| | Electron | **C# + csc.exe** |
|---|---|---|
| File size | ~180MB | **~80KB** |
| RAM | ~200MB | **~30MB** |
| Dependencies | Node + node_modules | **none** |

---

## Menu options

### Position
- **Lock position** — prevents accidental dragging
- **Snap to screen edges** — snaps when you drag it close (on by default)
- **Move to corner** — top/bottom, left/right
- **Move to monitor** — shown only if you have more than one screen

### Appearance
- **Always on top**
- **Opacity** — 50% / 70% / 85% / 100%
- **Font size** and **orientation** (vertical, as in the screenshot, or horizontal)
- **Match taskbar height** — measures your taskbar at launch and auto-scales the font to fit (on by default)
- **Match taskbar color** — samples the actual pixels of your taskbar, so it matches exactly what you see and follows your theme when you switch it (on by default)
- **Colors** — 5 ready-made presets, or customize every color individually (up arrow, down arrow, numbers, background)

### Measurement
- **Format** — auto (switches between KB/MB/GB based on speed) or pin it to one unit
- **Unit** — bytes `MBps` or bits `Mbps` (the difference is ×8)
- **Network adapter** — auto (busiest one) or pick a specific one

### Speed test
Measures ping, download, and upload against Cloudflare's test endpoints, using several parallel connections (a single connection understates real throughput on fast lines). The last result is saved and shown in the menu.

### Usage
Today / this month / all-time, with a reset button. Rolls over automatically at the start of each day and month.

> **Note:** the counter measures traffic on the network adapter, which includes local network traffic too, not just internet — exactly like Task Manager.

### Behavior
- **Click-through** — clicks pass through the widget to whatever's underneath
- **Hide during fullscreen** — disappears on its own during games and movies
- **Start with Windows**
- **Create Start Menu shortcut** — so Windows Search can find the app

---

## Where data is stored

In `%APPDATA%\NetSpeedMeter\`:

| File | Contents |
|---|---|
| `settings.ini` | All settings + position + last test result |
| `usage.ini` | Usage counters |
| `error.log` | Only written if something goes wrong (the app never shows you error dialogs) |

To reset everything: close the app and delete the folder.

---

## Technical notes

**Never interferes with other apps** — four layers:
- `WS_EX_NOACTIVATE` — never takes focus; if you're typing in another program and accidentally click it, your keystrokes keep going to that program
- `WS_EX_TOOLWINDOW` + `ShowInTaskbar=false` — invisible in the taskbar and Alt+Tab
- Click-through option
- Automatic fullscreen hiding via `SHQueryUserNotificationState` — the same query the taskbar itself uses, so it isn't fooled by a plain maximized window

**Single instance only** — via a `Mutex`; launching it twice exits quietly instead of opening a second widget.

**Measurement accuracy** — the division uses the actual elapsed time from a `Stopwatch`, not a fixed 1000ms, otherwise the numbers spike when the machine wakes from sleep. If the counter reading drops below the previous one (happens when the adapter resets), the delta is treated as zero instead of a huge bogus number.

**Lightweight by design** — the widget only redraws **when the displayed text actually changes**; a steady speed means zero redraws. Usage is written to disk every 60 seconds and on exit, not every second.

---

## Known issue: Windows Search can't find the app

Windows Search indexes **Start Menu shortcuts**, not any exe sitting in some folder. The app creates its own shortcut on first launch, and there's a manual option for it in the menu under **Behavior**.

**A real trap:** the `WScript.Shell` interface throws an exception when the save path contains non-Latin characters — like a OneDrive-synced Desktop folder (`C:\Users\...\OneDrive\<localized name>`). That's why the canonical copy lives at an ASCII path (like `D:\New\net-speed-meter\`) and the shortcut points there. The code also tries the short 8.3 path as a fallback, and finally **verifies** the saved shortcut actually resolves to a real file — deleting it instead of leaving a broken shortcut behind if not.
