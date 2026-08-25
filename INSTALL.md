# DentalX — Installation & Distribution Guide

How to package a build, get it onto another PC, and run it there for the first time. Every install is fully independent — no patient data is ever shared between PCs (each doctor keeps their own). For how to *use* the app once it's running, see [manual.md](manual.md).

---

## Part 1 — Packaging a build *(you, the developer)*

Requirements: the .NET 10 SDK on your own machine (already installed, since you're building DentalX).

1. Open a terminal in the project folder.
2. Run:
   ```
   .\publish.ps1
   ```
3. Wait 1–2 minutes. Output: `publish\DentalX_<date_time>.zip` (about 240 MB).

That zip is a complete, self-contained copy of DentalX — the receiving PC needs **nothing else installed** for it to run; the .NET runtime and every dependency (SQLite, OpenCV, the sensor SDK) are already bundled inside.

Re-run `publish.ps1` any time you've added features you want to hand out. Each run produces a fresh dated zip, and your own dev build (`bin\Debug`, run via `dotnet build`/`dotnet run`) is completely untouched by this — you can keep developing while doctors run their own separate copies.

---

## Part 2 — Getting the zip to another PC

The file is too big for most email attachments (~240 MB vs. the usual 20–25 MB limit), so use one of these instead:

- **USB flash drive** — fastest, no upload needed, and avoids the Windows "downloaded from the internet" warnings below. Recommended if you can hand it over physically.
- **A shared cloud folder** (Google Drive, OneDrive, Dropbox) — upload once, share the link.
- **A local network share**, if both PCs are on the same network.
- **WeTransfer** or similar, for a one-off transfer to someone remote.

---

## Part 3 — Installing on the new PC *(each doctor)*

### System requirements

- Windows 10 or 11, 64-bit
- ~500 MB free disk space (the app itself + room for captured X-rays)
- **No internet connection needed to run it** — fully offline
- The Hamamatsu sensor and its driver are only needed for real captures — everything else (Test capture, Load Image, all viewer/measurement tools) works fine with no hardware attached at all

### Steps

1. Copy the `.zip` file onto the new PC.
2. Right-click it → **Extract All...** → pick a folder (e.g. `Desktop\DentalX`) → **Extract**.
   > Run the app from the extracted folder — don't double-click the .exe straight out of the zip viewer without extracting first.
3. Open the extracted folder and double-click **DentalX.exe**.

### If Windows shows "Windows protected your PC" (SmartScreen)

Expected and harmless — it just means the app isn't signed with a paid publisher certificate yet, so Windows flags any new unrecognized app the first time it's run on a PC.

1. Click **More info**.
2. Click **Run anyway**.

This only appears once per PC, the first time DentalX.exe is launched there.

### First launch

- The app creates a brand-new, empty database automatically and shows a **one-time popup** with default login credentials: `admin` / `admin123`.
- Log in, then go straight to **Settings → Change Your Password**.
- Optionally set that doctor's own **Clinic Name** under Settings too, so the install feels like theirs.

Done — this PC now has its own fully independent copy: its own patients, its own images, its own login. It never talks to your dev machine or to any other doctor's install.

---

## Updating an existing install to a newer version

1. Publish a new zip as in Part 1.
2. On the doctor's PC, extract the new zip **over the same program folder**.
3. Re-launch DentalX.exe.

Their patient data (`Desktop\DentalX.db` and `Desktop\DentalX_Captures\`) lives outside the program folder, so replacing the program files never touches it — updating never loses their patients.

---

## Uninstalling

There's no installer to run in reverse — just delete the extracted DentalX folder. That removes the program only.

Patient data (`Desktop\DentalX.db`, `Desktop\DentalX_Captures\`) and the redundant backup copy in `C:\ProgramData\DentalX\` live outside that folder, so they survive deleting the program. Remove those too, manually, only if you genuinely want that PC's patient data gone for good.

---

## Troubleshooting

| Problem | What to do |
|---|---|
| Sensor won't connect / "CMOS_USB.dll" errors | Only matters for real captures — Load Image / Test capture work regardless. Make sure the Hamamatsu USB driver is installed on that PC. |
| Antivirus quarantines the .exe | Same root cause as SmartScreen above (unsigned app). Add an exclusion for the DentalX folder, or restore/allow it in your antivirus. |
| App won't start, no error shown | Make sure you extracted the **full** zip (all ~260 files) into one folder rather than copying just the .exe — it needs its neighboring .dll files sitting right next to it. |

---

Once it's running, see **[manual.md](manual.md)** for how to use every feature.
