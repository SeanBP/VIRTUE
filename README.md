# VIRTUE

VIRTUE is a standalone customizable event display for collider experiments for desktop or virtual reality (VR). The application is available for free on [GitHub](https://github.com/SeanBP/VIRTUE), [Steam](https://store.steampowered.com/app/2728380/VIRTUE), [Google Play](https://play.google.com/store/apps/details?id=com.Quantanaut.VIRTUE&hl=en_US), and [Zenodo](https://doi.org/10.5281/zenodo.10372110), and features a simplified model of the ePIC detector for the future Electron-Ion Collider, as well as simulated electron-proton collisions. New collision data and detector geometries can be uploaded into the program as JSON or FBX files within the application folder.

## Contents

- **ROOT2VIRTUE/** — Python conversion scripts (`ePIC2VIRTUE.py`, `FCS2VIRTUE.py`, `HMS2VIRTUE.py`) that read EDM4eic data products from CERN ROOT files and produce VIRTUE-compatible event JSON files. `RootFiles/` holds example input ROOT files.
- **VIRTUE Lite Builds/** — Pre-built VIRTUE Lite executables for Windows, macOS, and Linux.
- **VIRTUE Scripts/** — Mirrored C# source scripts from each of the four platform Unity projects (Mobile, Lite, Desktop, VR), included for reference and customization.
- **VIRTUE_User_Guide_V3_2_0.pdf** — The full user guide, covering system requirements, the JSON event/model/tour file formats, and how to build the Unity projects.
