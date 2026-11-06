# Windows setup

Build the production Release DLLs from the matching randomizer source. Use NSIS 3.11 or later with the x64 Unicode target available. From this directory:

```sh
./build.sh /path/to/release StarsOfDestiny-Setup.exe
```

The installer embeds `SuikodenRandomizer.dll` and `SuikodenRandomizer.Core.dll`. The website places the same executable beside each player's generated `agilen.suikoden.recruitment_randomizer.cfg`. Keep both files together when running setup.

Setup checks the game folder and supported game revision. It installs the two DLLs and the selected config, with previous copies under `SRR-Backups` in the game folder. It requires a working BepInEx installation.

For a signed release, sign and timestamp the built executable before publishing it and computing its release checksum.
