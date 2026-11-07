# Donald Duck Online Co-op

**English** · [Deutsch](README.de.md)

Online co-op mod for **Disney's Donald Duck: Goin' Quackers** (PC; called *Quack Attack* in Europe).
Two players play together over the internet in the same world: two Donalds, shared portals,
shared items, stars and progress, and a spectator mode with a live picture of the partner's game.

> **The game is not included.** Every player needs their own CD or an image of their own CD.
> This repository contains only the source code of the mod: no game files, no CD images and no
> data extracted from the game.

Unofficial fan project, not affiliated with Disney or Ubisoft.

> **Note:** The program's own windows and the friend instructions are currently in German.
> The mod was built and tested with the German re-release of the game; other language versions
> may not work.

## How it works

`DonaldOnline.exe` starts the original game and changes its memory while it runs: a second
Donald as a copy of the player, input only for the window in the foreground, portals that only
open when both players stand on them, and synchronised items, progress, lives and camera.
The connection runs over a temporary
[Cloudflare Quick Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/do-more-with-tunnels/trycloudflare/)
(`cloudflared` is downloaded from its official GitHub releases the first time you host).
The host gets an invite code and sends it to the other player.

## Use

Ready-made EXE + instructions: see **Releases**. Both players need exactly the same version
(the network protocol changes between versions).

### Windows Defender / SmartScreen
The program reads and writes the memory of another process and is not signed. Windows Defender
may therefore flag it (for example as `Behavior:Win32/DefenseEvasion.A!ml`). Whether you allow it
is your decision; the safest way is to build the EXE yourself from this source code.

## Build

Only needs the .NET Framework 4.x that comes with Windows; Visual Studio is not required:

```bat
build.cmd
```

Output: `build\DonaldOnline.exe`. `build.cmd test` also builds a test version
(`/define:TESTAUTO`) that can be driven through command files.

## Files

- `src\OnlineLauncher.cs`: start window (play alone, host or join an online game)
- `src\OnlineSession.cs`: session, network loop, level changes, co-op rules
- `src\RoomWire.cs`, `src\Tunnel.cs`, `src\VideoStream.cs`: protocol, tunnel, spectator picture
- `src\GameSetup.cs`, `src\AppPaths.cs`, `src\GameLocation.cs`, `src\GameInstance.cs`: setup, settings, log
- `src\GameBridge.cs`, `src\ReplicaFrame.cs`, `src\Respawn.cs`: connection to the game, second Donald
- `src\InputGuard.cs`, `src\InputExpressions.cs`: input control
- `src\PortalGate.cs`, `src\LevelExit.cs`, `src\WorldSync.cs`, `src\CoopLives.cs`, `src\Spectator.cs`, `src\NameTag.cs`: co-op logic
- `src\TestDriver.cs`: test builds only
- `resources\dgVoodoo.conf`, `resources\ubi.ini`: embedded settings (dgVoodoo2 window mode, game settings)

## Thanks

The structure of the OpenSpace engine (actors, states, input, "always" objects) was understood with
the help of these projects. No code was taken from them, only knowledge about data structures:

- [Raymap](https://github.com/byvar/raymap) by byvar and contributors (no license stated)
- [ACP_Ray2](https://github.com/spitfirex86/ACP_Ray2) by spitfirex86 (MIT license)
- [dgVoodoo2](https://github.com/dege-diosg/dgVoodoo2) by Dege: downloaded from its official page on request, not bundled

## License

No license chosen yet; all rights to the mod's own code remain with the author.
