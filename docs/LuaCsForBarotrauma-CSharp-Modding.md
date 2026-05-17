# LuaCsForBarotrauma C# Modding

LuaCsForBarotrauma adds two modding layers to Barotrauma: Lua scripting and C# plugin loading. This document focuses on the C# side: how it is loaded, what it can modify, where the real limits are, and how to build a small UI mod.

## How C# Mods Load

LuaCs treats C# code as an unrestricted content package resource. When Barotrauma loads enabled content packages, LuaCs reads the package metadata and collects C# resources from either `ModConfig.xml` entries or the legacy folder layout.

The load path is:

1. Barotrauma discovers an enabled content package from its `filelist.xml`.
2. LuaCs creates mod configuration data for the package.
3. Assembly resources are selected for the current target and platform.
4. Runtime source files are compiled with Roslyn, or precompiled `.dll` files are loaded directly.
5. The resulting assemblies are loaded into a per-package `AssemblyLoadContext`.
6. LuaCs finds non-abstract types that implement `IAssemblyPlugin`.
7. LuaCs instantiates those plugins and calls their lifecycle methods.

The main plugin contract is:

```cs
using System;
using Barotrauma.LuaCs;

public sealed class MyPlugin : IAssemblyPlugin
{
    public void PreInitPatching() { }
    public void Initialize() { }
    public void OnLoadCompleted() { }
    public void Dispose() { }
}
```

`Initialize()` is the usual place to set up your mod. `OnLoadCompleted()` runs after all plugins have been initialized, so it is better for cross-mod interaction. `Dispose()` must remove hooks, UI components, timers, and other state created by the mod. `PreInitPatching()` exists in the interface, but the in-repo documentation notes that pre-vanilla-content patching is not currently supported as a general feature.

## Supported C# Mod Forms

The easiest form is a runtime-compiled source mod:

```text
MyMod/
  filelist.xml
  ModConfig.xml
  CSharp/
    Client/
      ClientOnlyCode.cs
    Server/
      ServerOnlyCode.cs
    Shared/
      SharedCode.cs
```

`CSharp/Client` is compiled only by the client, `CSharp/Server` only by the server, and `CSharp/Shared` is included in both target assemblies. LuaCs also supports explicit `ModConfig.xml` entries, for example:

```xml
<?xml version="1.0" encoding="utf-8"?>
<ModConfig>
  <Assembly Folder="%ModDir%/CSharp/Client"
            IsScript="true"
            Target="Client"
            FriendlyName="MyMod.Client" />
</ModConfig>
```

### Hosted Servers Are Still Client Target

If LuaCsForBarotrauma is installed only in a client game, and that client hosts a multiplayer game, LuaCs is still running the client target inside the client executable. This is a listen/hosted server from the player's point of view, but it does not make `CSharp/Server` assemblies load in that client-side LuaCs environment.

In that setup:

- `CSharp/Client` code can run and can access client-side state, UI, previews, and local preferences.
- `CSharp/Server` code is not loaded unless LuaCs is installed and running on the actual server target.
- `GameMain.Client.IsServerOwner` can be true, but `GameMain.NetworkMember.IsServer` is still false in the client target.
- Changing local client state does not automatically change authoritative server or campaign-save state.

This matters for mods that need durable multiplayer changes. For example, an in-game character customizer can update the visible character and the local multiplayer preferences from `CSharp/Client`, but an existing multiplayer campaign character's saved appearance is owned by server-side campaign data. To persist that kind of change, the server target must receive the request and update the authoritative data, such as `MultiPlayerCampaign.GetClientCharacterData(client).CharacterInfo`, then mark the relevant campaign net flag dirty.

Do not treat "I am hosting" as equivalent to "my client-side C# assembly is a server assembly." If a feature must survive save/load, round transition, or campaign reload in multiplayer, design a server-side path and require LuaCs on the server side too.

For larger mods, you can ship precompiled assemblies. Legacy discovery looks for platform-specific folders such as:

```text
bin/Client/Windows/
bin/Client/Linux/
bin/Client/OSX/
bin/Server/Windows/
bin/Server/Linux/
bin/Server/OSX/
```

Assembly mods are easier to debug in an IDE and can use a fuller .NET project setup. Runtime source mods are easier to inspect, copy, and distribute for small examples.

## What Can Be Modified?

C# mods are not sandboxed. If the user enables C# support, plugin code runs in the game process and can access the CLR, LuaCs services, and Barotrauma types. In practice, this means a C# mod can do much more than XML content overrides:

- create, remove, or alter GUI components;
- inspect and mutate game objects;
- subscribe to LuaCs or game events;
- patch game methods with Harmony;
- load additional managed or native dependencies;
- read and write files where LuaCs allows mod file access.

That does not mean every change is safe or portable. The real limits are:

- **Client/server target:** client-only UI code does not exist on a dedicated server, and server gameplay state does not automatically exist on a client.
- **Networking:** changing local client state does not necessarily change authoritative server state.
- **Load timing:** some screens, entities, and content are created after plugin initialization.
- **Access level:** LuaCs tries to make modding practical through publicized references and internal access helpers, but private/internal implementation details remain fragile.
- **Game updates:** patches against method names, private fields, or exact UI trees can break when Barotrauma changes.
- **Trust and safety:** C# mods are unrestricted code. Users must explicitly enable C# because the code can crash the game or perform normal process-level actions.

Use the least invasive tool that solves the problem. Prefer public game APIs and normal GUI construction first. Use reflection or Harmony only when the behavior cannot be reached through a stable API.

## UI Mod Example

The example in `examples/UiOverlayExample` is a client-only runtime-compiled C# mod. It creates a small panel attached to `GUI.Canvas`, patches the relevant screens' `AddToGUIUpdateList` methods, refreshes two text lines while the panel is in the GUI update list, and cleans up the panel on unload.

Install it by copying `examples/UiOverlayExample` into Barotrauma's `LocalMods/UiOverlayExample`, enabling LuaCs C# support, and enabling the local content package.

The important parts are:

- `filelist.xml` creates the content package.
- `ModConfig.xml` tells LuaCs to compile `CSharp/Client` only on clients.
- `UiOverlayExample.cs` implements `IAssemblyPlugin`.
- The root UI component is a custom `GUIFrame` subclass that overrides `Update`.
- Harmony postfixes add the overlay to the GUI update list whenever common screens rebuild their GUI update list.
- `Dispose()` removes the root from the GUI update list, detaches it from `GUI.Canvas`, and unpatches Harmony.

Barotrauma's GUI update list is rebuilt often. Creating a component once is not enough to keep it visible; the component must be submitted again when the active screen adds its own GUI to the update list. This is why persistent UI mods typically patch `AddToGUIUpdateList` or use another recurring hook to call `AddToGUIUpdateList` on their root component.

## Why a Loaded UI Mod May Still Show Nothing

Seeing a log line such as `UiOverlayExample loaded.` only proves that LuaCs loaded and initialized the plugin. It does not prove that the GUI component is still in Barotrauma's active GUI update/draw list.

A useful comparison is the local mod `Sandbox Menu - Advanced Sandbox Tools for Barotrauma`. Its UI works because it creates its frame once, then patches screen GUI submission methods and re-adds the frame whenever those screens rebuild their GUI lists:

```lua
Hook.Patch("Barotrauma.GameScreen", "AddToGUIUpdateList", function()
    SandboxMenu.frame.AddToGUIUpdateList(false, 1)
end)

Hook.Patch("Barotrauma.SubEditorScreen", "AddToGUIUpdateList", function()
    SandboxMenu.frame.AddToGUIUpdateList(false, 1)
end)

Hook.Patch("Barotrauma.NetLobbyScreen", "AddToGUIUpdateList", function()
    SandboxMenu.frame.AddToGUIUpdateList(false, 1)
end)
```

The important behavior is not Lua vs. C#. The important behavior is repeated GUI submission. The broken version of this example only called `AddToGUIUpdateList()` once in `Initialize()`, so it could load successfully and then disappear as soon as Barotrauma refreshed the GUI list.

The C# example now follows the same pattern with Harmony postfix patches:

```cs
harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
```

The postfix calls the plugin instance, and the instance calls:

```cs
overlayRoot.AddToGUIUpdateList(ignoreChildren: false, order: 1);
```

Use this checklist when a UI mod loads but does not appear:

- Confirm the code is running on the client, not only on the server.
- Confirm the component has a parent under a visible GUI tree such as `GUI.Canvas`.
- Confirm it is submitted after the active screen rebuilds the GUI update list.
- Confirm the active screen type is actually patched. Some screens are outside the simple `Barotrauma.<Name>` namespace pattern.
- Confirm `Visible` is still `true`.
- Confirm the component is not hidden behind another full-screen GUI element; try a higher update order for overlays.
- Clean up with `RemoveFromGUIUpdateList()`, detach from the parent `RectTransform`, and unpatch Harmony in `Dispose()`.

## Character Editor Is a Separate Screen Type

The overlay did not appear in the character editor for a different reason than the first bug. The first bug was repeated GUI submission. The character editor bug was incomplete screen coverage.

`CharacterEditorScreen` lives in a nested namespace:

```cs
namespace Barotrauma.CharacterEditor
{
    class CharacterEditorScreen : EditorScreen
```

Its full type name is:

```text
Barotrauma.CharacterEditor.CharacterEditorScreen
```

It also has its own `AddToGUIUpdateList()` override:

```cs
public override void AddToGUIUpdateList()
{
    if (rightArea == null || leftArea == null) { return; }
    rightArea.AddToGUIUpdateList();
    leftArea.AddToGUIUpdateList();
    Wizard.instance?.AddToGUIUpdateList();
    ...
}
```

The original example patched `MainMenuScreen`, `GameScreen`, `SubEditorScreen`, and `NetLobbyScreen`, but not `CharacterEditorScreen`. When the character editor became the selected screen, Barotrauma called `Barotrauma.CharacterEditor.CharacterEditorScreen.AddToGUIUpdateList()`, and the overlay postfix never ran.

The fix is to patch the exact full type name:

```cs
PatchAddToGuiUpdateList("Barotrauma.CharacterEditor.CharacterEditorScreen", postfix);
```

Do not assume every screen follows `Barotrauma.<ClassName>`. When supporting more UI areas, inspect the source for the selected screen class and patch that class's actual `AddToGUIUpdateList()` method. Other editor screens, such as sprite, level, event, and server-list screens, also have their own overrides and may need explicit coverage if the overlay should appear there too.

To modify an existing UI instead of adding a new overlay, use one of these approaches:

- traverse an existing component tree with `GetAllChildren()`, `FindChild(...)`, or known screen/frame references;
- add children to a known `RectTransform`;
- patch the method that builds the original UI with Harmony and add your component in a postfix;
- keep references to anything you create so `Dispose()` can remove it.

UI code should normally live in `CSharp/Client`. Dedicated servers do not include the client GUI classes.
