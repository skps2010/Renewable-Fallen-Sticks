# Renewable Fallen Sticks

Renewable Fallen Sticks makes fallen sticks grow back near mature trees over time.

It works with naturally generated trees and trees grown from saplings. Sticks can regrow while an area is loaded, or when the area is loaded again after some time.

## How it works

At each check, the mod randomly selects a small surface area. This can be a natural surface or ground built by a player.

The mod counts the trees in that area. A tree is a naturally generated log with nearby leaves. The tree count is used to estimate the local forest density.

Forest density, tree count, and soil fertility are used to estimate how many fallen sticks the area should contain. If the existing number of sticks is below that target, the mod generates more sticks there.

## Installation

1. Place `renewablefallensticks.zip` in the server's `Mods` folder.
2. Start or restart the server.

## Configuration

The server creates this file after the first start:

```text
Vintage Story/ModConfig/renewablefallensticks.json
```

Default settings:

```json
{
  "EnableNotificationLog": false,
  "CheckIntervalHours": 24,
  "MaxCatchUpAttempts": 7,
  "MaxSticksPerAttempt": 4,
  "SamplesPerAttempt": 3,
  "SampleRadius": 4,
  "StickSpawnRadius": 1,
  "ReferenceTreesPerChunk": 70,
  "StickGroundCodes": ["soil", "soil-*", "forestfloor", "forestfloor-*"],
  "StickReplaceableCodes": ["tallgrass", "tallgrass-*", "snowlayer", "snowlayer-*"]
}
```

### Important settings

| Setting | Default | Description |
| --- | ---: | --- |
| `EnableNotificationLog` | `false` | Enables detailed server log messages. |
| `CheckIntervalHours` | `24` | How often sticks can regrow, in game hours. |
| `MaxCatchUpAttempts` | `7` | Maximum missed intervals processed after an area is loaded again. |
| `MaxSticksPerAttempt` | `4` | Maximum sticks generated during one check. |
| `SamplesPerAttempt` | `3` | Number of locations checked during one check. |
| `SampleRadius` | `4` | Area used to find nearby trees and sticks. |
| `StickSpawnRadius` | `1` | Spawn radius around the checked location. `1` means a 3x3 area. |
| `ReferenceTreesPerChunk` | `70` | Reference value used to estimate local forest density. |

The default ground blocks are soil and forest floor. The default replaceable blocks are tall grass and snow layers.

Changes take effect after restarting the server.

## Custom Block Codes

`StickGroundCodes` and `StickReplaceableCodes` support `*` wildcards.

```json
{
  "StickGroundCodes": ["soil", "soil-*", "mymod:richsoil-*"],
  "StickReplaceableCodes": ["tallgrass-*", "snowlayer-*", "mymod:groundplant-*"]
}
```

Include both forms when needed. For example, `soil-*` matches `soil-medium-normal`, but it does not match the base code `soil`.

## Notes

- A mature tree must have nearby leaves to be counted.
- The mod does not permanently track individual trees.
- Regrowth is random, so sticks may not appear at every check.
