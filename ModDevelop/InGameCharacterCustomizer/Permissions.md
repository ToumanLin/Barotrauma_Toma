[h1]In-Game Character Customizer: Server Setup[/h1]

This mod contains separate client-side and server-side C# assemblies. LuaCs and C# mod loading must be enabled on both sides for the complete mod to work.

[h2]Server Requirements[/h2]

[list]
[*]Start the server using LuaCsForBarotrauma.
[*]Enable [b]In Game Character Customizer[/b] in the server's enabled content packages.
[*]Enable C# mod loading in LuaCs.
[/list]

You can enable C# mod loading in the LuaCs settings or set the following option in [code]LuaCsConfig.xml[/code] in the game root directory:

[code]
<EnableCSharp>true</EnableCSharp>
[/code]

When the server assembly loads successfully, the server log contains:

[code]
InGameCharacterCustomizer server loaded.
[/code]

[h2]Client Requirements[/h2]

Every player who wants to use the customization interface must:

[list]
[*]Have the client-side version of LuaCsForBarotrauma installed.
[*]Download and enable the same version of this mod used by the server.
[*]Enable C# mod loading in the LuaCs settings, or select [b]Enable C# for this session[/b] when prompted while joining the server.
[/list]

When the client assembly loads successfully, the client log contains:

[code]
InGameCharacterCustomizer client loaded.
[/code]

[b]Important:[/b] If a player does not have client-side LuaCs or does not enable C# mod loading, the server can still run the server assembly, but that player will not see the Customize buttons or customization window and cannot use the mod.

[b]Security notice:[/b] LuaCs C# mods are not sandboxed. Only enable C# mods from servers and mod authors you trust.

[h2]Default Permission[/h2]

Players without an explicit permission entry use [code]CurrentCrew[/code] by default. This allows a player to customize only their own current living character.

Available modes:

[list]
[*][code]AnyCrew[/code] - the player may customize any living character in the current crew, including bots and other player characters.
[*][code]CurrentCrew[/code] - the player may customize only their own current living character.
[*][code]None[/code] - customization is disabled for the player.
[/list]

[h2]Permission Commands[/h2]

The server console and server owner can use these commands:

[code]
icc_setpermission <client> <AnyCrew|CurrentCrew|None>
icc_getpermission <client>
icc_listpermissions
icc_reloadpermissions
[/code]

Examples:

[code]
icc_setpermission "Alice" AnyCrew
icc_setpermission "Bob" CurrentCrew
icc_setpermission "Carol" None
icc_getpermission "Alice"
icc_listpermissions
[/code]

The [code]<client>[/code] argument may be a player name, session ID, account ID, or endpoint. Put player names containing spaces inside quotation marks.

Remote administrators require the native [code]ManagePermissions[/code] permission in addition to permission to execute the relevant console command. The server console and owner connection are allowed automatically.

[h2]Permission File[/h2]

Permissions are stored on the server at:

[code]
Data/InGameCharacterCustomizerPermissions.xml
[/code]

The file is created or updated automatically when [code]icc_setpermission[/code] is used. It can also be edited manually while the server is stopped.

Example:

[code]
<?xml version="1.0" encoding="utf-8"?>
<CustomizePermissions>
  <Client name="Alice" accountid="76561198000000000" mode="AnyCrew" />
  <Client name="Bob" accountid="76561198000000001" mode="CurrentCrew" />
  <Client name="Carol" accountid="76561198000000002" mode="None" />
</CustomizePermissions>
[/code]

If you edit the file while the server is running, apply the changes with:

[code]
icc_reloadpermissions
[/code]

[h2]Troubleshooting[/h2]

[list]
[*][b]The server loads the mod, but there is no Customize button:[/b] Verify that the client has client-side LuaCs installed, has enabled this mod, and has enabled C# mod loading.
[*][b]A player cannot customize anyone:[/b] Check their permission with [code]icc_getpermission <client>[/code]. A value of [code]None[/code] disables customization.
[*][b]A player can customize only themselves:[/b] This is the expected behavior for [code]CurrentCrew[/code]. Grant [code]AnyCrew[/code] only to trusted players.
[*][b]Manual permission-file changes do not take effect:[/b] Run [code]icc_reloadpermissions[/code] or restart the server.
[*][b]The mod reports a C# loading error:[/b] Confirm that [code]EnableCSharp[/code] is set to [code]true[/code] on the affected server or client.
[/list]
