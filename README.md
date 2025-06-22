# Prevue Guide Simulator

## What is it?
This is a simulator for the classic Amiga based Prevue Guide.

![Prevue Guide screenshot](/.readme/guide.png)

## How do I run it?
This uses .NET 9 and SDL 3 to run, so make sure you have .NET 9,  SDL3, SDL3_ttf, and SDL3_image installed. Once those are installed, `cd` into the main directory and run `dotnet run --project PrevueGuide/PrevueGuide.csproj`. You should be treated to a blue grid with nothing really happening except the time changing. Drop an `xmltv.xml` file (generated however you feel) onto the window, and then shortly there after listings should start to scroll.

**Note**: For Mac users, `brew install --cask dotnet-sdk; brew install sdl3 sdl3_image sdl3_ttf` should be sufficient with homebrew. *If* you're using an M1 mac, Homebrew will drop the SDL 3 libraries in a location where .net won't look for them (by deafult in `/opt/homebrew`). You can copy the `dylib` files in `libraries` into the `bin/(debug/release)/net9.0/` folder after you do an initial run and it should work as expected, or you can execute `dotnet run` with the following command to tell .net to pick the libraries up from `/opt/homebrew`:

`DYLD_LIBRARY_PATH="$(brew --prefix sdl3)/lib:$(brew --prefix sdl3_image)/lib:$(brew --prefix sdl3_ttf)/lib:$DYLD_LIBRARY_PATH" dotnet run`

## Contributing?
Make an issue or a PR. I don't have as much time to work on this as I'd like, so I'm going at a glacial pace right now. Any help would be appreciated! Admittedly, this started off as a proof of concept so the code is pretty messy in some areas, the comments may not make much sense, there's graphical glitches and features definitely missing, and it's hella unoptimized.

## Notes
This uses the [PrevueGrid font](https://ariweinstein.com/prevue/viewtopic.php?t=449) from @RudyValencia
