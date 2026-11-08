# Nuzlocke test 0.8.12

This standalone test starts a new campaign with a shuffled cast and a finite pool of 75 companion fighters. A confirmed battle KO retires that companion. A fresh reserve takes over the same story role after battle and inherits its level, equipment and runes, with full HP. Revival after a confirmed KO cannot restore a life. An automatic revival that prevents the native KO state does not retire a fighter.

Tir stays as the original protagonist for the entire campaign, with his chosen name and story rune. He is excluded from the companion pool, cannot swap into another role, and does not retire when KO'd. After a battle KO, he returns with 1 HP; a full-party retry restores him to full HP. IncludeTir is ignored in Nuzlocke mode. Ted and Odessa's temporary party slots still use the finite companion roster.

Future recruitments adjust when a reserve has already been used. After fresh reserves run out, surviving fighters on the bench can return. The active party shrinks as the roster is lost. A full party defeat retries the same encounter while companion replacements remain. Once all 75 companions are retired, Tir can continue alone; a full-party defeat then uses the normal game-over screen.

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

[Balance]
StartingGear = Balanced
```

Use Point = BeforeViktor to test from the later checkpoint. Balanced starting gear applies to new campaigns. Set StartingGear = Original to keep the replacement heroes' usual starting gear. Nuzlocke replacements inherit the fallen story slot's equipment and upgrades. You can continue a 0.8.9 through 0.8.11 Nuzlocke save with its existing equipment. Version 0.8.8 Nuzlocke campaigns are not supported. Other existing randomized saves retain their original campaign mode.

## Playtest

- Confirm the mode and reserve count appear in the in-game overlay.
- Let one fighter be KO'd, win the battle and inspect the replacement.
- Test a revival after KO.
- Let the full active party be defeated. Confirm the same encounter restarts with replacements, the battle becomes visible, and commands and attack animations work. Repeat a full-party defeat to test another retry.
- Recruit and swap party members, checking that retired fighters do not return.
- Make a manual save, quit completely and reload. Check the fighters and retirement count.
- KO Tir and confirm he remains the original protagonist after battle and after save/load. Then move between areas and trigger a scene.
- Confirm Tir keeps his chosen name and story rune and never appears as a companion replacement.

Deaths and assignments are stored with the campaign's native saves and matching BepInEx config data. Loading an older save restores its older run state.

Reserve-team retries reload shared battle resources before restarting the encounter. Diagnostics remain enabled. The setup is unsigned.

## Collect a crash report

Reproduce the crash, then launch the game once more. The next launch automatically packages the previous unfinished session. Open BepInEx/StarsOfDestiny-Diagnostics in the game folder and send the newest stars-crash-report ZIP, along with the area, battle and action immediately before the crash.

If the game still responds, press F6 to export a report immediately. The report path is written to BepInEx/LogOutput.log. Keep the diagnostic folder until the report has been collected.

Reports contain the randomizer's last recorded actions, managed exception details and available game logs or native crash files. A native crash can bypass managed exception handlers; an absent exception message does not establish the cause.
