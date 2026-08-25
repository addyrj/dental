# DentalX — User Manual

Dental X-Ray Imaging & Practice Management System (IOTtech Smart Products)

This manual walks through every screen and feature, step by step, in the order you'd normally touch them. Nothing described here is aspirational — if a feature isn't finished, it's labeled "coming soon" right in the app instead of pretending to work, and this manual calls that out too.

> New here, or setting this up on a doctor's PC for the first time? See **[INSTALL.md](INSTALL.md)** for how to get DentalX packaged, shared, and running — this manual picks up from there.

---

## 1. Logging In

1. Launch **DentalX.exe**.
2. Enter your **Username** and **Password**. Click the eye icon in the password field to reveal/hide what you typed.
3. Click **Login**.

**First run only:** if no staff accounts exist yet, the app creates one automatically —
`admin` / `admin123` — and shows you those credentials once in a popup. Change the password immediately (see [Settings → Change Your Password](#12-settings-admin-only)).

**Roles:** Admin, Dentist, Assistant. Only **Admin** accounts can reach **Backup & Restore** and **Settings** — everyone else sees a clear "restricted to Admin" message instead of the page.

---

## 2. The Shell (Header + Sidebar)

Every screen shares the same frame:

- **Header** — logo, app name, the sensor status pill (● Connected / Disconnected, with serial number once connected), a settings gear (shortcut to Settings), and your avatar (click to log out).
- **Left sidebar** — navigation to every module. Click **☰** at the top to collapse it to icons-only (click again to expand). Below the nav list, **Quick Actions** gives one-click shortcuts to New Patient, Import DICOM, Export Report, and Print.
- **Ctrl+P** anywhere in the app opens **Quick Patient Search** — type a name, press Enter or double-click a result to jump straight to that patient's profile, Esc to close.

---

## 3. Dashboard

Landing page after login. Shows:

- **Today's Captures**, **Total Patients**, **New Patients (7d)** — all real counts from your database.
- **AI Findings** tile — honestly reads "model not connected" (see [AI Analysis](#7-ai-analysis)).
- **Recent Captures** list and a **Storage** tile (image count + MB used).
- **Go to Imaging** button (top right) jumps straight into capture mode.

---

## 4. Imaging — Capture, Enhance, Measure

This is the main workspace. Everything here is organized around one image at a time.

### 4.1 Getting an image onto the screen

- **⬤ Connect Sensor** — connects to the Hamamatsu CMOS sensor over USB. The header pill turns green and shows the serial number once connected.
- **◉ Capture X-Ray** — fires a real capture (only enabled once the sensor is connected). If the sensor detects no actual X-ray signal (`maxVal < 100`), the app tells you plainly — *"No X-ray detected (noise only)"* — rather than showing noise as if it were a real image.
- **🧪 Test** — generates a synthetic test image with no sensor needed, for trying the tools out.
- **📂 Load Image** — opens any existing .jpg/.png/.bmp/.tif from disk.
- **Modality tabs** (All / Intraoral / Panoramic / Cephalometric / 3D CBCT) filter the gallery strip at the bottom.

### 4.2 The viewer toolstrip (left column)

If you don't see every icon, the strip scrolls — drag the scrollbar or use the mouse wheel over it.

| Icon | Tool | How to use |
|---|---|---|
| ✋ | Pan | Default — click and drag the image |
| ↖ | Select | Reserved for future use |
| ⊕ / ⊖ | Zoom In/Out | Or use `+` / `-` keys, or scroll wheel |
| 🔍 | **Magnifier** | See [4.4](#44-magnifier) |
| ↻ | Rotate | 90° per click, or press `R` |
| ☀ | Window/Level | Right-click and drag: drag right = brightness, drag up = contrast |
| ⬒ | Crop | See [4.5](#45-crop) |
| ✎ / T | Annotate | See [4.6](#46-annotate) |
| 📏 | **Measure** | See [4.7](#47-measure--distance-tooth-length-angle-calibration) |
| ▭ | ROI rectangle | Not built yet — clicking it says so |

### 4.3 Zoom bar (below the viewer)

`−` / `+` / percentage readout, a **Fit** button (resets zoom/pan/rotation to default), a slider, Auto-Enhance (☀), Invert (◑), Reset View (⛶), and **⤢ Full Zoom** — opens the image large enough to fill the viewer area while staying completely visible (or just double-click the image). Press `Esc` or click the ✕ to close.

### 4.4 Magnifier

1. Click **🔍** in the toolstrip. A small panel appears with **1x / 2x / 4x** and a calibration-style status.
2. Hover anywhere over the image — a circular loupe appears, connected to your cursor by two lines (like a magnifying-glass callout), showing that exact spot magnified.
3. Click 1x/2x/4x any time to change magnification — it updates instantly even without moving the mouse.
4. Click **🔍** again (or the ✕ in the panel) to turn it off.

The loupe shows real, cropped pixel data from the source image — not a fake zoomed screenshot — so it stays sharp and accurate at any zoom level.

### 4.5 Crop

1. Click **⬒**, then drag a rectangle on the image.
2. Click **✓ Apply Crop** to genuinely crop the pixel data (not just visually hide the edges), or **✕ Cancel**.
3. Use **Save/Attach** afterward to keep the cropped version.

### 4.6 Annotate

1. Click **✎** (or **T**), then click anywhere on the image to type a text label.
2. Right-click a label to remove it.
3. Labels are an overlay — they display on screen but aren't baked into the saved file (same as the Measure overlays).

### 4.7 Measure — Distance, Tooth Length, Angle, Calibration

Click **📏** to open the measurement panel. Pick one of four modes:

- **📏 Distance** — click two points anywhere on the image.
- **🦷 Tooth Length** — click the crown reference point, then the root apex. You'll be asked for an (optional) FDI tooth number; the label reads *"Tooth 36: 21.4 mm (radiographic)"*. The **"(radiographic)"** tag is intentional — a 2D X-ray measurement is never exactly the true anatomical length once projection/magnification is accounted for, so the app never overstates its own precision.
- **∠ Angle** — click three points: first point → vertex → second point. Shows the angle in degrees.
- **📐 Calibrate** — click two points across something of a **known real-world length** (e.g. a ruler visible in the shot), then type that length in mm. The app computes your sensor's mm-per-pixel scale from that and remembers it — every Distance/Tooth Length measurement from then on shows real millimeters instead of raw pixels. (You can also set this directly under [Settings](#12-settings-admin-only) if you already know your sensor's pixel pitch.)

**Correcting a measurement:** every line has small circular handles at its endpoints — drag one to adjust; the label recalculates live. Right-click any line or its label to delete that measurement.

**Measurements are saved** to the database per image and reload automatically whenever you reopen that image. They also show up under that image's entry in the patient's [Examination History](#52-patient-profile).

### 4.8 Enhancement panel (right side)

- **Brightness / Contrast (CLAHE) / Gamma / Sharpness** sliders, with **Apply** / **Reset**.
- One-click presets: **Auto, Hi-Con, Caries, Endo, Perio, Bone, Thermal, Edge** — each applies a different processing/colormap suited to a specific reading task.
- Press `Space` to toggle between the original and enhanced version of the current image; `I` inverts.

### 4.9 AI Analysis mini-panel

Shows placeholder rows (Caries, Bone Loss, Periapical Radiolucency, Calculus) all marked **"—"** with the header **"AI Detection: coming soon."** This is deliberate — there's no detection model wired in yet, so nothing here is ever a fabricated finding.

### 4.10 Attach to Patient

This is how an image becomes part of a patient's permanent record:

1. Search for the patient in the **Patient** panel (bottom right) and click their row to select them — or click **＋ New Patient** to create one on the spot.
2. Click **📎 Attach Image** (or press `Ctrl+S`).

At this moment the app also **organizes the file on disk** — see [§14, Where Your Files Live](#14-where-your-files-live) — and the image disappears from the general capture gallery because it now belongs to that patient.

### 4.11 Gallery

The strip at the bottom lists images filtered by the active modality tab. Toggle **⊞ Grid View** for a larger thumbnail layout. Click any thumbnail to load it into the viewer.

### 4.12 Deleting an image

**🗑 Delete** on the toolbar permanently removes the currently loaded file from disk (with a confirmation prompt) — including any measurements saved against it.

---

## 5. Patients

### 5.1 Patients List

- **Search** by name or phone.
- **＋ New Patient** — Title, First/Last Name, Gender, Age, DOB, Phone, Address, Country, Notes.
- **⬆ Import CSV / ⬇ Export CSV** — bulk add or back up your patient list. Import recognizes columns named Name (required), Age, Gender, Phone, Title, DOB, Address, Country, Notes, in any order.
- **View Profile →** opens the full patient record.

### 5.2 Patient Profile

- **Header** — click the avatar circle to set a patient photo.
- **Stat tiles**: Examinations, **Visits** (count of *completed* appointments — real data, not a repeated exam count), Treatment Items, and Reports (marked "soon" — there's no per-patient PDF generator wired to this exact tile yet; use the global [Reports](#8-reports) page instead).
- **Patient Details** — editable fields, same set as New Patient.
- **Examination History** — one card per attached image: modality, date, an editable **Tooth #** field (auto-saves), a free-text notes box (auto-saves), and any **Measurements** taken on that image.
- **Treatment Plan** — add an item with Tooth # (optional), description, Priority (Low/Medium/High), Status (Planned/Recommended/Completed); shows in a list with the same fields.
- **Tooth Chart** (FDI notation, 32 teeth) — **left-click** a tooth to cycle its condition: Healthy → Caries → Restoration → Root Canal → Missing → Extraction → Implant (color-coded). **Right-click** a tooth that has linked images to see them listed.

---

## 6. Appointments

- **＋ New Appointment** — pick a patient (type to search), date, time, reason.
- Grid shows Date/Time/Patient/Reason/Status with **✓ Complete**, **✕ Cancel**, and **👤** (open that patient) actions per row.
- Completing an appointment here is what feeds the **Visits** stat on that patient's profile.

---

## 7. AI Analysis

A standalone page to load any image and see the same "coming soon" placeholder findings as the Imaging panel, plus an explicit note: *"Requires a trained ONNX model + ONNX Runtime — not included yet."* Nothing here fabricates a diagnosis.

---

## 8. Reports

1. Pick a patient from the list on the left (search box above it).
2. Pick one of their examinations from the list that appears.
3. Click **🖨 Generate & Print / Save as PDF** — in the print dialog, choose **"Microsoft Print to PDF"** as the printer to save a PDF file instead of printing on paper.

---

## 9. Compare Images

Load two images side by side (**📂 Choose Left/Right Image**). **Sync zoom & pan** (on by default) keeps both views moving together as you zoom/pan on either side — turn it off to inspect each independently. `−` / `+` / **Reset View** control zoom.

---

## 10. DICOM Import / Export

- **Import**: choose a `.dcm` file — a preview and its tags display. Click **💾 Save as Capture** to bring it into the normal Imaging workflow. Only **uncompressed** grayscale DICOM is decoded; compressed transfer syntaxes (JPEG/JPEG2000/RLE) show a clear error instead of a garbled image.
- **Export**: choose an existing image, fill in Patient Name/ID and Modality (Intraoral/Panoramic/Computed Radiography/Digital X-Ray), then **Export as DICOM (.dcm)...**.

---

## 11. Device Management

Lists hardware DentalX can talk to. Only the real, connected Hamamatsu sensor is ever shown as **Connected** — everything else reads **Not configured**. Click **⟳ Refresh** to re-check. Nothing on this page is simulated.

---

## 12. Settings *(Admin only)*

- **General** → Clinic Name.
- **Measurement Calibration** → set your sensor's pixel pitch (microns) directly, if you already know it — same effect as running the Calibrate tool in Imaging. Leave blank to keep working in pixels.
- **Change Your Password** → current + new + confirm.
- **Users** → **＋ Add User** (Full Name, Username, Role, Password) to create additional staff logins.

---

## 13. Backup & Restore *(Admin only)*

- **💾 Run Backup Now...** — saves your entire database **and every captured image (including all patient folders)** into one `.zip` file, wherever you choose to save it.
- **♻ Restore From Backup...** — replaces your *current* database and images with the contents of a backup zip. This overwrites current data, so it asks for confirmation — use it deliberately, not casually.

Run a backup regularly, especially before major changes (a new install, moving to a new PC, etc.).

---

## 14. Where Your Files Live

- **Newly captured / loaded images**, before being attached to anyone, sit in:
  `Desktop\DentalX_Captures\`
- **The moment you Attach an image to a patient** (§4.10), it moves into that patient's own folder:
  `Desktop\DentalX_Captures\Patients\P{00007}_{PatientName}\`
  The folder name always starts with the Patient ID, so it never collides with another patient of the same name and never breaks if a name is corrected later.
- **A second, automatic copy** of every attached image is also kept in:
  `C:\ProgramData\DentalX\Patients\P{00007}_{PatientName}\`
  This is a silent redundancy copy — the app never reads from it, it's just there in case something happens to the Desktop copy. It is **not** a substitute for running real backups.
- **Patient photos** (profile avatars) are stored separately in `Desktop\DentalX_Captures\patient_photos\`.
- **The database** (all patients, exams, treatments, appointments, measurements, settings) is one file: `Desktop\DentalX.db`.

Images already attached *before* this folder system existed remain wherever they were saved — only newly attached images get organized this way, so nothing that already worked was disturbed.

---

## 15. Keyboard Shortcuts (Imaging page)

| Key | Action |
|---|---|
| `Space` | Toggle original ↔ enhanced |
| `I` | Invert |
| `R` | Rotate 90° |
| `+` / `-` | Zoom in / out |
| `0` | Fit to view |
| `Ctrl+S` | Attach current image to the selected patient |
| `Ctrl+Z` | Reset enhancement sliders |
| `Esc` | Close Full Zoom |
| `Ctrl+P` | Quick Patient Search (works app-wide, not just Imaging) |

---

## A Note on Honesty

Throughout DentalX, anything not yet real is labeled as such instead of faked: AI findings show "—" and "coming soon," Device Management only marks *actually connected* hardware, and measurements are labeled "radiographic" rather than claiming clinical-grade anatomical accuracy. If a screen says something isn't built yet, that's accurate — not a bug.
