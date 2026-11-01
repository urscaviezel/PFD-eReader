# PFD eReader

**Ein immersiver eBook- und Comic-Reader für das Play For Dream MR Headset (PFDMR).**

*[English version](README.md)*

Lies deine Bücher als echtes 3D-Buch, das du vor dir hältst – im Passthrough, im Dunkeln oder in einem von drei gemütlichen 3D-Räumen. Blättere mit dem Controller, setz dich aufs Sofa, mach Musik an oder schau YouTube auf dem Fernseher im Raum.

> **Beta** – aktuelle Version: **v0.2.1**. Rückmeldungen und Fehlerberichte gerne über [Issues](https://github.com/urscaviezel/PFD-eReader/issues).

![Lesen im Loft](docs/images/room-loft.png)

## Funktionen

- **Formate:** EPUB, FB2 / FB2.ZIP, PDF, TXT, HTML, Markdown – Comics als CBZ/ZIP, CBR (RAR), CB7 (7-Zip) und CBT (TAR)
- **3D-Buch** mit echtem Umblättern, Lesezeichen, Kapitelliste, einstellbarer Schriftgrösse, hellem/dunklem Lesemodus, PDF-Zoom
- **Umgebungen:** Passthrough, Dunkel, 3D-Räume (Loft, Spa Suite, Resort Suite) mit Tag/Nacht, eigene 360°-Panoramen
- **Bewegen** in den Räumen (gehen, drehen, teleportieren), hinsetzen auf Sofas und Stühle
- **Fernseher** mit YouTube (TV-Oberfläche, Anmeldung per Code), räumlicher Ton
- **Sound:** Umgebungsgeräusche und eigene Musik (MP3, M4A, OGG, FLAC, WAV …)
- **Import** vom PC/NAS (SMB-Netzwerkfreigaben) oder aus den Ordnern des Headsets
- **Sprachen:** Deutsch, Englisch, Französisch
- **Leistung:** Foveated Rendering, übernimmt die Bildwiederholrate des Headsets (72/80 Hz empfohlen)
- **Updates:** Die App sucht auf GitHub nach neuen Versionen und installiert sie nach deiner Bestätigung

| Bibliothek | Einstellungen |
|---|---|
| ![Bibliothek](docs/images/library.png) | ![Einstellungen](docs/images/settings.png) |

## Installation

1. `PFD-eReader-vX.Y.Z.apk` aus dem [neuesten Release](https://github.com/urscaviezel/PFD-eReader/releases/latest) herunterladen.
2. Auf dem Headset installieren, z. B. mit `adb install PFD-eReader-vX.Y.Z.apk` (Entwicklermodus) oder einem Sideloading-Programm.
3. **PFD eReader** starten und den Zugriff auf den Ordner **eBooks** erlauben.
4. Bücher in den Ordner **eBooks** auf dem Headset kopieren (oder *Einstellungen → Import* verwenden).

Die bebilderte Anleitung (DE/EN/FR) steckt in der App (**?** in der Bibliothek) und liegt jedem Release bei.

### Aktualisieren

*Einstellungen → App-Info → Nach Updates suchen* (oder automatisch einmal täglich). Beim ersten Mal fragt Android, ob PFD eReader „unbekannte Apps installieren“ darf.

> **Hinweis für Beta-Tester früherer Versionen:** Ab v0.2.0 ist die App mit einem neuen Release-Schlüssel signiert. Bitte die alte Version einmalig deinstallieren, bevor du v0.2.0 installierst. Deine Bücher im eBooks-Ordner bleiben erhalten.

## Aus dem Quellcode bauen

Voraussetzungen:

- **Unity 6000.6.5f1** (6.6) mit Android Build Support
- **Play For Dream Unity SDK 3.1.0** (`com.yvr.*`-Pakete) aus dem Play-For-Dream-Entwicklerportal. `Packages/manifest.json` erwartet es unter `../../_Shared/PFDMR-SDK/UnitySDK_3_1_0/` – sonst die `file:`-Pfade anpassen.

Optional, aus Lizenzgründen nicht in diesem Repository:

- **3D WebView for Android** von Vuplex (kommerziell) – für den Fernseher. Nach `Assets/Vuplex/` importieren; das Define `PFD_VUPLEX` wird dann automatisch gesetzt. Ohne Vuplex baut die App ohne Fernseher.
- **3D-Räume** (von Fab) und **Himmels-Panoramen** (Unity Asset Store). Ohne sie bietet die App Passthrough, Dunkel und eigene Panoramen. Die Editor-Werkzeuge zum Aufbau der Räume sind enthalten (`Assets/PFDReader/Editor/RoomTools.cs`).

Projekt öffnen, auf Android umstellen und bauen (`File → Build Profiles`). Release-Builds werden mit einem Schlüssel signiert, der nicht im Repository liegt – verwende deinen eigenen.

## Lizenz

Der Quellcode von PFD eReader steht unter der **[MIT-Lizenz](LICENSE)**.
Komponenten von Drittanbietern behalten ihre eigenen Lizenzen – siehe **[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)**.

PFD eReader ist ein unabhängiges Projekt und steht in keiner Verbindung zu Play For Dream, Google/YouTube, Vuplex oder Epic Games.
