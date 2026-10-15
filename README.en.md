# Genome Guardian 2 Cheat

[中文 README](README.md)

A cheat tool for the single-player Steam game **Genome Guardian 2**, in two parts:

- **Save editor**: while the game is closed, edit Research points, Enzymes, unlocks, research levels, the current run's Biomass and Health, or any raw value in the save.
- **Live god mode**: while the game is running, turn on invulnerability or lock your health, with no save or restart needed.

> This is an unofficial fan-made tool and has nothing to do with the developer, Alpherior. Use it in single-player only, at your own risk. Genome Guardian 2 names and in-game text are © Alpherior.

The interface is in Traditional Chinese. Item names are shown in the game's own Simplified Chinese and English; unlock conditions and effect descriptions are shown in Simplified Chinese only.

![Resources tab](docs/resources.png)

## Features

| Tab | What it does |
|---|---|
| 資源 Resources | Research points and Enzymes. The 目前這一局 (current run) box: Biomass; Health, Armour and Shield plus their max bonuses; extra Lives, Rerolls, Bleaches, Banishes, Skips; current Wave |
| 解鎖 Unlocks | Guardians, hulls, platforms, barrels, brackets, cores, motherboards, gadgets, mods, paints, patterns, cursors, crosshairs, cones, every mode's depths 1–5, plus Daily Dive, DEPTH X, Uracil and Zone. Select an item to see its original unlock condition; click it again or press Space to toggle it |
| 研究 Research | Every research node's level. "全部升滿" (max all) never goes past the game's level cap |
| 進階 Advanced | Edit every value in the save, with a search box that filters by key name. Editing a dangerous field shows a warning first |
| 即時 Live | God mode, stronger god mode, and a health/shield/armour lock |

"一鍵全解鎖" (unlock everything) in the toolbar unlocks every item the save lists and maxes every research node that has a cap (uncapped nodes, cap 0, are left as they are).

| Unlocks | Live |
|---|---|
| ![Unlocks tab](docs/unlocks.png) | ![Live tab](docs/live.png) |

| Research | Advanced |
|---|---|
| ![Research tab](docs/research.png) | ![Advanced tab](docs/advanced.png) |

## Download and use

Requires 64-bit Windows 10 (1903 or later) or Windows 11. It uses the built-in .NET Framework 4.8, so there is nothing to install.

1. Download the zip from [Releases](../../releases), unzip it and run `GG2修改器.exe`.
2. It finds the save automatically: `%USERPROFILE%\AppData\LocalLow\Alpherior\Genome Guardian 2\genome-guardian-2.txt`.

The exe is unsigned, and the Live tab writes to the game's memory, so Windows SmartScreen or antivirus software may warn about it.

### Editing the save (Resources, Unlocks, Research, Advanced)

1. **Close the game completely.** The tool won't save while the game runs, because the game overwrites the save from memory when it quits.
2. Make your changes.
3. Click "儲存到遊戲" (save to game), then start the game.

The 目前這一局 (current run) box only works while a run is in progress: start a run, quit the game at the shop screen, and the tool picks it up automatically. It doesn't reload automatically while you have unsaved edits; click "重新讀取" (reload) instead. If the game saved again while you were editing, the tool asks before saving so old data doesn't overwrite newer progress.

Before every save, the original files are backed up to the `存檔備份` folder next to the exe ("開啟備份資料夾" opens it). To roll back, click "還原備份…" (restore backup) and pick a folder; the save you're replacing is backed up first too.

### Live god mode

1. With the game running, open the tool and switch to "即時（遊戲中）".
2. Tick "無敵" (god mode). The current wave and health are shown at the top.
3. Keep the tool open while you play. Untick it or close the tool to go back to normal.

| Option | Effect |
|---|---|
| 無敵 God mode | Hits deal no damage, and reaching 0 health doesn't kill you. Uses the game's own built-in invincibility flag; no blinking and no stat changes |
| 加強無敵 Stronger | Also blocks the "touching a Deposit self-destructs you" effect. The ship blinks and the "time invulnerable" stat goes up |
| 鎖血 Lock | Health, Shield and Armour never go down: they're held at the highest value seen since you ticked it (or since the run started), restored every 100 ms. A single lethal hit can still kill you, so combine it with god mode |

God mode doesn't stop: the anomaly "Gadget Charges +1, self-destruct if charges remain at wave end", the Jester guardian's random win/lose roll after wave 16, or abandoning the run from the pause menu.

## Things to know

- **Steam achievements**: the game checks achievements on every load, so anything you unlock may unlock matching achievements on your real Steam account. This can't be undone.
- **Steam Cloud**: if Steam asks which save to keep when you start the game, choose the local one.
- **Game updates**: unlocks, research and the Advanced tab are normally unaffected. The current-run field positions, the name table and the Live tab's memory addresses all come from analysing Steam build 25938646 and may not match after an update. The Live tab checks class names when it attaches and stops writing (with a warning) if they don't match; the current-run box has no such check.
- Special unlocks such as DEPTH X are re-evaluated by the game, so a change may not stick.
- In the Advanced tab, wrong values in `fresh`, `build`, `dev`, `modeId`, `saveEpoch`, `version`, the story-state fields, `nanobot*` or `runSave` can reset your profile, make the load fail and fall back to the backup, void the current run, jump the story, or open the developer console (which can wipe all your Steam achievements). The tool warns you first.

## Building

The code only uses C# 5, so Windows' built-in compiler is enough. Double-click:

```bat
src\build.bat
```

This creates `GG2修改器.exe` in the project folder.

| File | Contents |
|---|---|
| `src/GG2Editor.cs` | Save reading/writing, the whole UI, and the Live tab's memory addresses and logic |
| `src/GameMemory.cs` | Generic helper for reading/writing another process's memory |
| `src/names.json` | Item, research and mode names, effect descriptions, original unlock conditions and research level caps (the game's own Simplified Chinese and English; embedded into the exe at build time) |

## How it works

**Save file**: `genome-guardian-2.txt` (plus `-backup` and `-rescue` copies) is JSON with every byte XORed against the repeating ASCII key `alphakenneth`. The JSON has four sections, `StringData`, `IntData`, `FloatData` and `BoolData`, each a list of `{"Key":…,"Value":…}`. There is no checksum or signature: the game loads any valid JSON whose `saveEpoch` is above 0. The tool writes the same format back (byte-identical to the original when nothing changed), writes all three copies, and bumps `saveEpoch` by 1.

**Live god mode**: `Nanobot.Damageable()`, which decides whether you can take damage, first checks a static flag, `Test.runInvincible`. The same flag also skips the 0-health death check. The tool sets it to true every 100 ms. The ship's own `invulnerable` bool is recomputed by the game every frame, so writing it does nothing; the stronger mode holds the `invulnerableTime` timer instead.
