Below are the XML attributes/properties that can be used on:

```xml
<sprite>
  <LightComponent ... />
  <override ... />
</sprite>
```

## `<LightComponent>`

Includes properties from `LightComponent`, its `Powered` base class, and the light-source params parsed from the same XML element.

| Property | Type | Default Value | Description |
| :--- | :--- | :--- | :--- |
| `range` | `float` | `100.0` | Range of emitted light. Higher values are more performance-intensive. |
| `lightcolor` | `Color` | `255,255,255,255` | Color of emitted light, formatted `R,G,B,A`. |
| `castshadows` | `bool` | `true` | Whether structures cast shadows from this light. Disabled automatically for behind-sub lights. |
| `drawbehindsubs` | `bool` | `false` | Draws the light behind submarines; faster, no shadows. |
| `ison` | `bool` | `false` | Whether the light is currently on. |
| `flicker` | `float` | `0.0` | Flicker strength. `0` = none, `1` = alternates between dark and full brightness. |
| `flickerspeed` | `float` | `1.0` | Speed of flickering. |
| `pulsefrequency` | `float` | `0.0` | Pulse frequency in Hz. `0` = no pulsing. |
| `pulseamount` | `float` | `0.0` | Pulse strength. `0` = none, `1` = alternates between full brightness and off. |
| `blinkfrequency` | `float` | `0.0` | Blink frequency in Hz. `0` = no blinking. |
| `ignorecontinuoustoggle` | `bool` | `false` | If true, continuous toggle signals only toggle once. |
| `alphablend` | `bool` | `true` | Also draws the light sprite with alpha blending on the item. |
| `lightoffset` | `Vector2` | `0,0` | Offset of the light from the item position, in pixels. |
| `lightspritescale` | `float` | `1.0` | Scale of the light sprite. |
| `minvoltage` | `float` | `0.5` | Minimum voltage required for the device to function. |
| `powerconsumption` | `float` | `0.0` | Power drawn from the electrical grid while active. |
| `isactive` | `bool` | `false` | Whether the device/component is active. Inactive devices consume no power. |
| `currpowerconsumption` | `float` | `0.0` | Current power consumption. Mostly intended for conditionals/status effects. |
| `voltage` | `float` | `0.0` | Current voltage. Mostly intended for conditionals/status effects. |
| `vulnerabletoemp` | `bool` | `true` | Whether the item can be damaged by EMPs. |
| `inheritparentisactive` | `bool` | `true` | If this is a child component, whether it inherits the parent component’s active state. |
| `pickingtime` | `float` | `0.0` | Time required to pick up/interact with the item, in seconds. |
| `pickingmsg` | `string` | `""` | Text shown on the progress bar while picking. |
| `canbepicked` | `bool` | `false` | Whether the item can be picked/interacted with. |
| `drawhudwhenequipped` | `bool` | `false` | Whether the item interface is drawn while equipped. |
| `lockguiframeposition` | `bool` | `false` | Locks GUI frame position. |
| `guiframeoffset` | `Point` | `0,0` | GUI frame offset. |
| `canbeselected` | `bool` | `false` | Whether the item can be selected by interacting with it. |
| `canbecombined` | `bool` | `false` | Whether the item can be combined with same-type items. |
| `removeoncombined` | `bool` | `false` | Remove item if combining drops condition to 0. |
| `characterusable` | `bool` | `false` | Whether characters can trigger the item’s use action. |
| `allowingameediting` | `bool` | `true` | Whether in-game editable properties can be edited. |
| `deleteonuse` | `bool` | `false` | Whether the item is deleted when used. |
| `msg` | `string` | `""` | Text shown next to the item when highlighted. |
| `combatpriority` | `float` | `0.0` | AI combat usefulness score. |
| `manuallyselectedsound` | `int` | `0` | Selected sound index when manual sound selection is used. |
| `updatewhenbroken` | `bool` | `false` | If true, component keeps normal functionality when item condition reaches 0. |
| `isactiveconditionalcomparison` | `LogicalOperatorType` | `And` | Logical operator for active conditionals. |

Additional light-source attributes parsed by the client light renderer:

| Property | Type | Default Value | Description |
| :--- | :--- | :--- | :--- |
| `color` | `Color` | `1.0,1.0,1.0,1.0` | Light source color used by `LightSourceParams`. Usually prefer `lightcolor` on `LightComponent`. |
| `scale` | `float` | `1.0` | Scale multiplier for the light sprite/texture. |
| `offset` | `Vector2` | `0,0` | Offset used by the light source params. |
| `rotation` | `float` | `0.0` | Light texture/parameter rotation in degrees. |
| `directional` | `bool` | `false` | Directional light behavior; prevents shadows behind the light direction. |

Valid child elements inside `<LightComponent>` include:

```xml
<sprite ... />
<lightsprite ... />
<deformablesprite ... />
<lighttexture ... />
<conditional ... />
```

## `<override>`

`<override>` is not a component. It is a child of `<sprite>` used only for localization-specific sprite texture/source overrides.

| Property | Type | Default Value | Description |
| :--- | :--- | :--- | :--- |
| `language` | `LanguageIdentifier` | `""` | Language this override applies to. Must match the current game language. |
| `texture` | `string` | `""` | Replacement texture path for this language. |
| `sourcerect` | `Rectangle` | `0,0,0,0` | Replacement source rectangle, formatted `X,Y,Width,Height`. |
| `sheetindex` | `Point` | `0,0` | Sprite-sheet cell index used to derive `sourcerect`. |
| `sheetelementsize` | `Point` | `0,0` | Size of one sprite-sheet cell, used with `sheetindex`. |

Only `texture`, `sourcerect`, `sheetindex`, and `sheetelementsize` are actually read from `<override>` by `Sprite.cs`; normal sprite attributes like `origin`, `size`, `depth`, and `compress` stay on the parent `<sprite>`.