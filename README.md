# Rip It! Card Collector — Shift Bind

A BepInEx 6 (IL2CPP) mod for **Rip It! Card Collector** (v1.0.7, released 30/10/2026).

## What it does

Hold **Shift** and **Left Click** a card to instantly handle it:

- **Duplicate card** → it is sold immediately, using the exact same pipeline as dropping it on the sell slot (money, sell popup, statistics).
- **New card** → the binder automatically flips to the card's page and the card is auto-placed in its correct slot (same slot assignment used by the in-game Binder Bot).

The normal click/drag behavior is untouched when Shift is not held.

## Requirements

- Game: `Rip It! Card Collector` v1.0.7 (extracted)
- [BepInEx 6 IL2CPP](https://github.com/BepInEx/BepInEx) already installed in the game folder
- .NET SDK 6.0+ to build (or just use the release DLL)

## Installation

1. Copy `RipIt.ShiftBind.dll` into `BepInEx/plugins/RipIt-ShiftBind/` inside the game folder.
2. Launch the game once. The binder will flip pages and place cards when you Shift+Click them.
3. Logs go to `BepInEx/LogOutput.log` (look for lines prefixed `Rip It! Shift Bind`).

## Building from source

```powershell
dotnet build -c Release
```

The project references the game's interop assemblies from `BepInEx/interop` and the
BepInEx core assemblies from `BepInEx/core`. If your game is in a different folder,
override it:

```powershell
dotnet build -c Release -p:GameDir="X:\Path\To\Game"
```

The plugin DLL is produced at `bin/Release/net6.0/RipIt.ShiftBind.dll`.

## How it works (technical notes)

- Harmony **Prefix** on `DragManager.Click(InputAction.CallbackContext)` detects Shift+Left Click
  and consumes the event so the normal drag never starts.
- Duplicate detection reuses `CardFindManager.CheckIsDupe(CardInfo)`.
- Selling reuses `SellSlot.Sell(DraggableCard)` (the manual sell-slot pipeline).
- Auto-placement reuses `BinderPages.SetRoundedPageBinderBot(page)` + `DraggableCard.AutoBindCard(BinderPages)`,
  the same calls the in-game Binder Bot makes.
- A tiny state machine driven by a Harmony **Postfix** on `DragManager.Update` waits for the
  flip animation to finish (8 s timeout, one flip retry) before placing the card.

## Compatibility

- Requires BepInEx 6 IL2CPP (net6 plugins).
- Built against game v1.0.7; other versions may need recompiling against their own interop assemblies.

## License

MIT
