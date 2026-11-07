# Nuzlocke test 0.8.8

This standalone test starts a new campaign with a shuffled cast and a finite pool of 76 fighters. A confirmed battle KO retires that fighter. A fresh reserve takes over the same story role after battle and inherits its level, equipment and runes, with full HP. Revival after a confirmed KO cannot restore a life. An automatic revival that prevents the native KO state does not retire a fighter.

The protagonist keeps the chosen name and story rune. By default Tir starts as himself, then his slot can receive a replacement if he is KO'd. Set IncludeTir to true to shuffle him from the beginning. Ted and Odessa's temporary party slots also use the finite roster in this mode.

Future recruitments adjust when a reserve has already been used. After fresh reserves run out, surviving fighters on the bench can return. The active party shrinks as the roster is lost. The final survivor can occupy the protagonist's slot so exploration remains possible. A full party defeat retries the same encounter with remaining fighters. A defeated roster of all 76 ends the run.

## Install and configure

Back up game saves and BepInEx/config. Extract the entire download, close the game, then open StarsOfDestiny-Setup.exe and select the game folder. BepInEx 6 IL2CPP must already be installed and initialized once.

The included config selects this test.

```ini
[Shuffle]
Seed = nuzlocke-test
Mode = Nuzlocke
IncludeTir = false

[Start]
Point = Opening
```

Use Point = BeforeViktor to test from the later checkpoint. Start a separate New Game. Existing randomized saves retain their original campaign mode.

## Playtest

- Confirm the mode and reserve count appear in the in-game overlay.
- Let one fighter be KO'd, win the battle and inspect the replacement.
- Test a revival after KO and a full active-party defeat.
- Recruit and swap party members, checking that retired fighters do not return.
- Make a manual save, quit completely and reload. Check the fighters and retirement count.
- Check the protagonist's rune after replacing his fighter.

Deaths and assignments are stored with the campaign's native saves and matching BepInEx config data. Loading an older save restores its older run state.

Managed checks and a production build are available. The new battle transitions, full-wipe retry, and special scripted battles need live Windows playtesting. The setup is unsigned.

If the test stops, press F6 if the game responds and send BepInEx/LogOutput.log plus a description of the battle and action.
