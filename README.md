# Prevue Guide Simulator

## What is it?
This is a simulator for the classic Amiga based Prevue Guide.

![Prevue Guide screenshot](/.readme/guide.png)

## How do I run it?
This uses .NET 6 and SDL to run, so make sure you have .NET 6,  SDL, SDL_ttf, and SDL_image installed. For Mac users, `brew install --cask dotnet-sdk; brew install sdl2 sdl2_image sdl2_ttf` should 
be sufficient with homebrew. Once those are installed, `cd` into the main directory and run `dotnet run --project PrevueGuide/PrevueGuide.csproj`. You should be treated to a blue grid with nothing 
really happening except the time changing. Drop an `xmltv.xml` file (generated however you feel) onto the window, and then shortly there after listings should start to scroll.

## Contributing?
Make an issue or a PR. I don't have as much time to work on this as I'd like, so I'm going at a glacial pace right now. Any help would be appreciated! Admittedly, this started off as a proof of 
concept so the code is pretty messy in some areas, the comments may not make much sense, there's graphical glitches and features definitely missing, and it's hella unoptimized.

## Notes
This uses the [PrevueGrid font](https://ariweinstein.com/prevue/viewtopic.php?t=449) from @RudyValencia

This also uses [Dear Imgui Sharp](https://github.com/Sewer56/DearImguiSharp) which builds upon [cimgui](https://github.com/cimgui/cimgui) and [Dear Imgui](https://github.com/ocornut/imgui). It references a specific version of [CppSharp.Runtime](https://github.com/Sewer56/DearImguiSharp/blob/master/DearImguiSharp/deps/CppSharp.Runtime.dll) that is different than the version available in NuGet, so be sure to grab that file and place it in `deps` if you're manually creating the dependencies.

As well, it requires a special build of cimgui that exposes the SDL renderer implementaiton, which is included for osx-x64. Building instructions for this were:
- Clone https://github.com/rickz0rz/cimgui following the instructions in `README.md`
- Compile `backend_test/example_sdl_sdlrenderer` (`cd backend_test/example_sdl_sdlrenderer && cmake . && make`)
- Copy the resultant dylib file into this project as `cimgui.dylib` in the appropriate folder in `deps`
