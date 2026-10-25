# FNAF 2 Controls Mod � preview

A 32-bit drop-in DLL for the supplied Windows FNAF 2 build. No launcher or .NET installation is needed to play.

## Intended controls

- Hold left mouse for flashlight. Original Ctrl still works.
- Press A to toggle the mask.
- Press S to toggle cameras.
- Switching between mask and cameras closes the current one before opening the other, using the original animations.
- Hold the original wind button on any camera to wind the music box. Left-click winding takes priority over the new flashlight shortcut.
- Escape during a night returns to the title menu.
- Borderless, non-exclusive fullscreen with a 16:9 window.

The original mouse controls remain available. The game still controls battery use, enemy behavior, music-box drain, and restrictions during attacks.

## Install / remove

Close the game. Place `winmm.dll` beside `FiveNightsatFreddys2.exe`, then start the game normally. To remove the mod, close the game and remove this DLL. The original executable is not modified.

Only executable SHA-256 `84DB9972C70AE02BA9596E1BE624F2942545EE939611E7FCD593B75A67D41AC6` is currently supported. An unsupported executable runs without the mod changes. `fnaf2-mod.log` beside the DLL records validation and window setup.

## Validation status

The first build failed to start the game. The current revision defers the night patch until the game window exists; startup and gameplay with this revision are not yet confirmed. This is an experimental preview, not a verified release.

Compiled as native x86. The standalone loader check passed, including timer and audio-device forwarding. The patched data parses successfully; all image and sound data and the other 26 frames are unchanged. Patch application was checked with reads of different sizes. Gameplay and fullscreen behavior have not yet been tested in the game.

## Developer build

Requires .NET 10 SDK, Visual Studio C++ Build Tools (x86), and a Windows SDK. Run `build.cmd`. Output: `build/winmm.dll`. `DOTNET_EXE` can point to a portable .NET 10 SDK.

`resources/night.patch` is an XOR delta for the supported executable's night events. The DLL validates the executable before changing reads in memory. The native bootstrap forwards WinMM exports to the Windows system library and initializes the managed native code outside the loader lock. No game assets or executable are included in the mod archive.
