# Wearable Sprite XML Properties

Here is a complete list of all the XML properties that can be used inside a `<sprite>` element when it is defined within a `<Wearable>` component in Barotrauma.

Because `WearableSprite` is built on top of the base `Sprite` class, the properties are divided into **Base Sprite Properties** (used for rendering) and **Wearable-Specific Properties** (used for attaching and interacting with the character's body and other clothes).

## Base Sprite Properties

| Property | Type | Default Value | Description |
| :--- | :--- | :--- | :--- |
| **`texture`** | `string` | `""` | The file path to the sprite's texture. |
| **`name`** | `string` | `null` | Optional name identifier for the sprite. |
| **`sourcerect`** | `Rectangle` | `0,0,0,0` | The specific rectangular area of the texture to draw. Formatted as `X,Y,Width,Height`. |
| **`sheetindex`** | `Point` | `-1,-1` | Uses a grid-based approach to select the `sourcerect`. Requires `sheetelementsize` to work. Formatted as `X,Y` (e.g., `0,0` is top-left). |
| **`sheetelementsize`** | `Point` | `0,0` | The dimensions of a single cell in a sprite sheet. Used alongside `sheetindex`. Formatted as `X,Y`. |
| **`origin`** | `Vector2` | `0.5,0.5` | The relative pivot/origin point for the sprite, from `0.0` to `1.0`. `0.5,0.5` means the center of the sprite. |
| **`size`** | `Vector2` | `1.0,1.0` | Size multiplier of the drawn sprite (scales the `sourcerect` dimensions). |
| **`depth`** | `float` | `0.001` | The rendering depth, clamped between `0.001` and `0.999`. |
| **`compress`** | `bool` | `true` | Whether the loaded texture should be compressed in memory. |

## Wearable-Specific Properties

| Property | Type | Default Value | Description |
| :--- | :--- | :--- | :--- |
| **`limb`** | `LimbType` | `"Head"` | Which limb this wearable attaches to (e.g., `Head`, `Torso`, `LeftArm`, `RightArm`, `LeftLeg`, `RightLeg`, `RightHand`, `LeftHand`, `RightFoot`, `LeftFoot`, `Waist`). |
| **`hidelimb`** | `bool` | `false` | If true, the limb this sprite is attached to will be visually hidden. |
| **`hideotherwearables`** | `bool` | `false` | If true, completely hides any other wearables that are below this one (in terms of depth). |
| **`alphaclipotherwearables`** | `bool` | `false` | If true, applies alpha clipping to obscure wearables below this one instead of hiding them completely. |
| **`canbehiddenbyotherwearables`**| `bool` | `true` | Determines if this specific wearable sprite is allowed to be hidden by items worn on top of it. |
| **`canbehiddenbyitem`** | `string` | `empty` | A comma-separated list of item identifiers/tags that are specifically allowed to hide this wearable. |
| **`hidewearablesoftype`** | `string` | `null` | Comma-separated list of wearable types to specifically hide when this sprite is worn. Valid types: `Item`, `Hair`, `Beard`, `Moustache`, `FaceAttachment`, `Husk`, `Herpes`. |
| **`inheritlimbdepth`** | `bool` | `true` | Whether this sprite should render at the exact same depth as the limb it's attached to. |
| **`depthlimb`** | `LimbType` | `"None"` | If you want to inherit depth from a limb, but a *different* limb than the one the sprite is physically attached to, specify it here. |
| **`inheritscale`** | `bool` | `false`* | Inherits the character/limb's overall scaling. *(Default is `false` for Items, but `true` for Hair/Beards/Husk appendages).* `inherittexturescale` functions as an alias. |
| **`ignorelimbscale`** | `bool` | `false` | Explicitly prevents the sprite from scaling with the character's limb. |
| **`ignoretexturescale`** | `bool` | `false` | Explicitly ignores texture scale adjustments. |
| **`ignoreragdollscale`** | `bool` | `false` | Explicitly ignores ragdoll-specific scale modifiers. |
| **`inheritorigin`** | `bool` | `false`* | Automatically uses the origin point of the attached limb. *(Default is `false` for Items, `true` for Hair/Beards).* |
| **`inheritsourcerect`**| `bool` | `false`* | Automatically pulls the source rect from the attached limb. *(Default is `false` for Items, `true` for Hair/Beards).* |
| **`scale`** | `float` | `1.0` | A static scaling multiplier specifically for this wearable sprite. |
| **`rotation`** | `float` | `0.0` | Additional rotation offset in **degrees**. |
| **`sound`** | `string` | `""` | A sound alias/file to play when this wearable sprite is active. |

## Nested Elements

Additionally, `<sprite>` elements inside `<Wearable>` can have nested child nodes:
* **`<LightComponent>`**: You can attach one or more light components inside the sprite if you want the wearable to emit light (e.g., a diving mask or a headlamp).
