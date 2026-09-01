# In Game Character Customizer permissions

The server keeps per-player permissions in `Data/InGameCharacterCustomizerPermissions.xml`.
The default for a player that has no entry is `CurrentCrew`.

The server owner can run these commands:

```text
icc_setpermission <client> <AnyCrew|CurrentCrew|None>
icc_getpermission <client>
icc_listpermissions
icc_reloadpermissions
```

`<client>` can be the player's name, session ID, account ID, or endpoint. Quote names
that contain spaces. Remote use of the commands requires the native
`ManagePermissions` permission in addition to the normal console-command permission.

The file is written automatically when `icc_setpermission` is used. It can also be
edited while the server is stopped, for example:

```xml
<?xml version="1.0" encoding="utf-8"?>
<CustomizePermissions>
  <Client name="Alice" accountid="76561198000000000" mode="AnyCrew" />
  <Client name="Bob" accountid="76561198000000001" mode="CurrentCrew" />
  <Client name="Carol" accountid="76561198000000002" mode="None" />
</CustomizePermissions>
```

Run `icc_reloadpermissions` after editing the file while the server is running.
