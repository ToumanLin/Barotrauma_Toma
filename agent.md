# Agent Notes

This repository is a LuaCsForBarotrauma/Barotrauma workspace with local mod-development examples under `ModDevelop/` and reference notes under `docs/`.

## Project Orientation

- `Barotrauma/` contains the game source split into client, server, shared code, tests, and assets that are available in this checkout. Make your assumptions based on the game source code.
- `ModDevelop/` contains local content packages and LuaCs C# mod examples.
- `docs/LuaCsForBarotrauma-CSharp-Modding.md` explains how LuaCs C# mods load, how client/server targets behave, and how Barotrauma GUI mods must keep themselves visible.

## Build And Test Guidance

- Do not use `dotnet build` for LuaCs runtime-compiled C# mods. The local docs explicitly say: "Do not use Dot net build!"

## Code Style

- Match existing C# style: Microsoft-style naming conventions, explicit cleanup, and conservative use of reflection/Harmony.
- Prefer public Barotrauma/LuaCs APIs before reflection or Harmony patches.
- Keep changes scoped to the mod/package or source area requested.
- For XML, preserve Barotrauma's existing attribute casing and formatting where possible.
- Use structured XML APIs for non-trivial XML edits rather than fragile string replacement.
- Multi-language support, use game native way.
- Separate code files when the file is too long.