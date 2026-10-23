# Emulator COBRA

[English](README.md) · **Română**

Un emulator al calculatorului **COBRA**, un calculator personal românesc proiectat la Brașov în anii '80, scris de la
zero în C# pe .NET 10. Vine cu o aplicație desktop pentru Windows și Linux.

## Ce este COBRA

COBRA a fost proiectat la ITCI Brașov, institutul de cercetare pentru hardware și software de calculatoare din Brașov.
Prototipul a fost prezentat în 1986, iar calculatorul a intrat în producție în 1988. Este construit în jurul unui
**Zilog Z80** și este compatibil cu **ZX Spectrum**, dar a fost gândit și ca un mic calculator profesional:

- **80 KB de RAM**: 64 KB de DRAM plus 16 KB de memorie video, comutate între trei configurații hardware (pornire, BASIC
  și CP/M).
- O **interfață de dischetă** cu un controler Intel 8272A și un Z80-CTC, pentru până la patru unități.
- Un **ROM de BOOT** care pornește unul dintre sistemele din **ROM-ul de SISTEM**: **BASIC** Sinclair, **OPUS**, sistemul
  de operare pe disc **DEVIL**, sau **CP/M 2.2** cu un ecran text de **80 de coloane**.
- O interfață de casetofon, un difuzor și două porturi de joystick Kempston.

S-au fabricat doar câteva sute de calculatoare. Astăzi calculatorul este documentat, și reconstruit de pasionați, prin
[CoBra Project](https://cobrasov.com/CoBra%20Project/index.html), care publică manualele, schemele, ROM-urile și
imaginile de dischetă. Acest emulator este construit după documentația de acolo, iar ROM-urile și dischetele de sistem pe
care le livrează provin tot de acolo.

## Ce rulează

- **BOOT-ul standard de 80K**, **Unified Boot** (cu CP/M direct din ROM) și **placa originală de 64K** cu BOOT-ul ei.
- **BASIC** și **OPUS** din ROM-ul de SISTEM.
- **DEVIL**: catalogul dischetei (`CAT`), încărcarea și salvarea programelor pe dischetă.
- **CP/M 2.2** de pe dischetă și din ROM, pe ecranul de 80 de coloane: `DIR`, salvarea și rularea unui program.
- **Butonul NMI**, care salvează ecranul pe casetă și se întoarce în program.
- **Caseta**: salvarea și încărcarea programelor, fișiere `.tap` la intrare și la ieșire.
- **Sunetul** difuzorului, **tastatura** pe tastele PC-ului și **joystick-ul Kempston** pe săgeți.
- **Patru unități de dischetă** cu imagini TeleDisk (`.td0`), HFE și brute (`.img`), dischete goale DEVIL și CP/M, și
  salvarea unei dischete înapoi într-un fișier.

## Instalare

### Windows 10 / 11 (x64)

Deschideți **PowerShell** și rulați:

```powershell
irm https://raw.githubusercontent.com/mteletin/cobra/main/install.ps1 | iex
```

Scriptul descarcă cel mai nou `setup.exe` din [Releases](https://github.com/mteletin/cobra/releases), îi verifică
SHA-256 față de amprenta pe care o publică GitHub și îl instalează pentru utilizatorul dumneavoastră. Nu are nevoie de
drepturi de administrator.

- Pentru instalare manuală, descărcați `cobra-emu-<versiune>-setup.exe` din Releases și rulați-l. Programul de instalare
  nu este semnat, așa că Windows SmartScreen poate afișa „Windows protected your PC": alegeți **More info**, apoi
  **Run anyway**.
- Pentru o anumită versiune: `$env:COBRA_VERSION = '0.1.4'` înainte de comandă.
- Pentru a vedea asistentul de instalare în loc de instalarea silențioasă: `$env:COBRA_WIZARD = '1'` înainte de comandă.

| ce | unde |
|---|---|
| programul | `%LOCALAPPDATA%\Programs\COBRA\app` (în asistent puteți alege alt dosar) |
| imaginile de dischetă | `%LOCALAPPDATA%\Programs\COBRA\disks`, și în meniul Start: **COBRA > Disk images** |
| scurtături | meniul Start și desktop: **COBRA** |
| dezinstalare | Settings > Apps > **COBRA** |

### Ubuntu (x64)

```bash
curl -fsSL https://raw.githubusercontent.com/mteletin/cobra/main/install.sh | bash
```

Scriptul descarcă cel mai nou `linux-x64.run` din Releases, îi verifică SHA-256 față de amprenta pe care o publică GitHub
și îl rulează. Nu are nevoie de `sudo`: totul ajunge în dosarul dumneavoastră personal. .NET și SDL2 sunt incluse.
Aplicația mai are nevoie de `libfontconfig1` din sistem: Ubuntu Desktop îl are; pe o instalare minimală sau de server,
rulați întâi `sudo apt install libfontconfig1`.

- Pentru dezinstalare: `curl -fsSL https://raw.githubusercontent.com/mteletin/cobra/main/install.sh | bash -s -- --uninstall`.
- Pentru o anumită versiune: `... | COBRA_VERSION=0.1.4 bash`.
- Pentru instalare manuală, descărcați `cobra-emu-<versiune>-linux-x64.run` din Releases și rulați
  `bash cobra-emu-<versiune>-linux-x64.run`.

| ce | unde |
|---|---|
| programul | `~/.local/share/cobra-emu/<versiune>` (cu o legătură `current` spre cea mai nouă) |
| imaginile de dischetă | `~/.local/share/cobra-emu/current/disks` |
| lansare | `cobra` (în `~/.local/bin`), și **COBRA** în meniul de aplicații |

Dacă imediat după prima instalare comanda `cobra` nu este găsită, ieșiți din sesiune și intrați din nou (Ubuntu adaugă
`~/.local/bin` în `PATH` la autentificare), sau rulați `~/.local/bin/cobra`.

### Dischetele incluse

| fișier | ce este |
|---|---|
| `CPM-80-columns-system.img` | dischetă de sistem CP/M 2.2 cu ecranul de 80 de coloane |
| `DEVIL-UTILS1.TD0`, `DEVIL-UTILS3.TD0` | dischete cu utilitare DEVIL |

Pe Windows, rularea din nou a programului de instalare actualizează programul și păstrează dosarul de dischete așa cum
l-ați lăsat, chiar și după o dezinstalare.

## Folosirea emulatorului

Calculatorul pornește la ecranul BOOT-ului. Pe **BOOT-ul standard de 80K**:

| tasta | pornește |
|---|---|
| **B** | BASIC |
| **W** | OPUS |
| **D** | sistemul de pe discheta din unitatea 0 (DEVIL sau CP/M) |

**Unified Boot** (Machine > Reset from the Unified Boot) are propriul meniu, iar acolo **CTRL+0** pornește CP/M din
ROM-ul de SISTEM, fără dischetă.

- **Machine:** repornire de pe oricare dintre cele trei BOOT-uri, **butonul NMI** (și **F12**), comutatorul R/W al zonei
  BASIC și pauza.
- **Disks:** pentru fiecare dintre cele patru unități, introducerea unei imagini de dischetă, a unei dischete goale DEVIL
  sau CP/M 720K, salvarea dischetei într-un fișier sau scoaterea ei.
- **Tape:** introducerea unui fișier `.tap`, pornire și oprire, și salvarea a ceea ce a înregistrat calculatorul.
- **Input:** săgețile și Ctrl din dreapta ca joystick Kempston.
- **View:** mărirea imaginii.

Ca primă încercare: puneți `CPM-80-columns-system.img` în unitatea 0, reporniți de pe BOOT-ul standard de 80K, apăsați
**D** și scrieți `DIR` la promptul `A>`.

## Compilare din surse

Aveți nevoie de [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), iar pe Linux de `libfontconfig1`.

```bash
git clone https://github.com/mteletin/cobra.git
cd cobra
dotnet build Cobra.slnx
dotnet run --project src/Cobra.App
```

| dosar | ce conține |
|---|---|
| `src/Hc2000.Z80` | procesorul Z80, comun cu emulatorul HC2000 |
| `src/Cobra.Machine` | calculatorul: memoria și configurațiile ei, 8255, tastatura, caseta, placa de dischetă (8272A și Z80-CTC) și cititoarele de imagini de dischetă |
| `src/Cobra.Video` | imaginea controlerului video |
| `src/Cobra.App` | aplicația desktop (Avalonia) |
| `roms/` | imaginile ROM-urilor de BOOT și de SISTEM |
| `disks/` | dischetele de sistem pe care le livrează programele de instalare |
| `deploy/sdl2/` | SDL2 2.32.10 pentru sunet |

Compilarea copiază ROM-urile și SDL2 lângă aplicație. Sunetul folosește SDL2 2.32.10, iar aplicația verifică exact
această versiune; `--sound-dump fisier.wav` scrie în plus sunetul calculatorului într-un fișier.

## Mulțumiri

COBRA a fost proiectat la ITCI Brașov de Vasile Prodan, Sorin Finichiu, Bernd Hansgeorg Wagner, Marcel Arefia, Mircea
Pop, Mircea Ungur și Alexandru Antal, sub coordonarea lui Gheorghe Toacşe (CoBra Hardware Manual, 1988). Documentația,
ROM-urile și imaginile de dischetă sunt păstrate de [CoBra Project](https://cobrasov.com/CoBra%20Project/index.html).
