# Lorelei and the Sane Controls

This is a small mod that aims to somewhat improve the experience of navigating the numerous puzzles of The Letztes Jahr Hotel.

More specifically, it makes it so that for simple (and most common) locks - padlocks, roman numeral locks, etc - the up 
and down button rotate the dials, and the "interact" button confirms the decision. This also means that you can back out 
of the puzzle without navigating to the padlock's shackle.

I didn't touch some of the more difficult locks (like the Red Maze Door), but I hope to revisit it and see if I can 
figure out how to add additional bindings for the game to use. Maybe even a map shortcut? One can dream (most likely I'll get bored and won't do it though).

# How to install

## On Windows

Download the zip archive from [Releases](https://github.com/graynk/LoreleiAndSaneControls/releases) page and extract it to the game's root directory (in Steam it's Right Mouse Button - Manage - Browse local files).

## On Steam Deck

The game uses [BepInEx](https://docs.bepinex.dev/index.html) to inject the custom code, which in turn [relies](https://docs.bepinex.dev/articles/advanced/proton_wine.html) on `winhttp.dll` proxy DLL.
That means you have to do additional steps

1. Switch to Desktop Mode
2. Download the zip archive from Releases page and extract it to the game's root directory
3. Install protontricks via Discover app and launch it
4. Choose Lorelei and the Laser Eyes
5. Select the default wine prefix
6. Run winecfg
7. In the Libraries tab add a new override for `winhttp.dll` and click OK

# How to build

1. Install [.NET SDK](https://dotnet.microsoft.com/en-us/download)
2. Clone this repo
3. Create a `lib` directory in the project
4. Copy `Assembly-CSharp.dll` from `path\to\game\Lorelei and the Laser Eyes_Data\Managed` to `lib`
5. Run `dotnet build --configuration Release`
6. Copy resulting DLL from `LoreleiAndSaneControls\bin\Release\net46\` to `path\to\game\BepInEx\plugins` 
