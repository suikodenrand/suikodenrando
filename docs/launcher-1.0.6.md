# Stars of Destiny Launcher 1.0.6

Launcher 1.0.6 accompanies randomizer 0.14.5. The setup and manual Windows packages include it. Players using the plugins-only package can extract the standalone launcher ZIP beside the game executable.

- **Apply settings** checks changes from other launcher windows. If two edits would enable incompatible battle options, the launcher reports the conflict and keeps the staged edits available for correction.
- **Load settings preset** validates the combined settings and writes known section and key names using the spelling the game expects. Updates also find a key in a later repeated section.
- Seed validation follows the game's configuration reader. The launcher reports spellings that would change when BepInEx saves the configuration.
- **Activate ATB preview** verifies the paired public S1 component and preserves pending configuration edits until you apply them.
- **Save & launch game** starts the executable in the selected game folder.

Existing randomizer seeds and save bindings continue to use their existing mappings. Close the game before applying settings. [Release downloads](https://github.com/suikodenrand/suikodenrando/releases/tag/v0.14.5)
