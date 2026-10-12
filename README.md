# Cartur's Blacksmithing

More from the Crafting skill you already have, for weapons and armour. Gear you forge lasts longer,
you count as extra workbench and forge upgrades, at 70 you repair from your bag, and at 80 your gear
takes one more upgrade.

Inspired by **Smoothbrain's [Blacksmithing](https://thunderstore.io/c/valheim/p/Smoothbrain/Blacksmithing/)**
(retired in 2026), by Smoothbrain and [blaxxun-boop](https://github.com/blaxxun-boop/Blacksmithing) -
credit to them for the idea and the design. This is a separate mod written for Valheim 1.0.

## What it does

Valheim 1.0 has a Crafting skill of its own - it rises as you craft, repair and build. Everything here
builds on it, so there is no second skill to grind.

| Crafting level | You get |
| --- | --- |
| any | Weapons and armour you craft last longer - double durability at 100. The item remembers its smith's level, and an upgrade keeps the higher of the old and new. |
| 10 / 20 | You count as one / two extra workbench upgrades. |
| 30 / 40 | One / two extra forge upgrades. |
| 50 / 60 | One / two extra black forge and galdr table upgrades. |
| 70 | Repair your gear from the inventory, with no station nearby. |
| 80 | Weapons and armour take one upgrade past their normal maximum, at 3x the materials. |

The extra durability shows on the tooltip.

## Settings

`BepInEx/config/com.jekkle.valheim.carturblacksmithing.cfg` - every level above can be changed or
turned off (0), plus:

| Setting | Default | What it does |
| --- | --- | --- |
| `DurabilityBonus` | 1 | Extra durability at Crafting 100. 1 = double, 0 = vanilla. |
| `UpgradePrice` | 3 | Material cost of the extra upgrade, as a multiple of a normal one. |
| `EnforceVersion` | true | On a server, refuse clients on a different version. |

Install it on the server too and its numbers apply to everyone.

## Install

r2modman or Thunderstore Mod Manager. Needs BepInEx.
