# BCn Texture Compression

Client-side LuaCs C# mod that replaces Barotrauma's runtime DXT5/BC3 encoder with
[BCnEncoder.NET](https://github.com/Nominom/BCnEncoder.NET) 2.3.0.

## Behavior

- A Harmony prefix intercepts future calls to `TextureLoader.CompressDxt5` while preserving the game's existing compression switches, dimension checks, texture format, and upload path.
- After all plugins load, one background thread snapshots already-cached Sprite textures, decodes each source image, and encodes BC3 at `Balanced` quality.
- GPU texture creation, cache replacement, Sprite reference replacement, and old texture disposal happen on the main thread as one compare-before-swap commit.
- Migration is sequential and BCnEncoder's internal parallelism is disabled to limit CPU spikes and peak memory use.
- If a future encode fails, that call falls back to Barotrauma's original encoder. If a cached texture changes while migration is preparing it, the prepared result is discarded.

Only textures managed by `Sprite.textureRefCounts` are migrated. Textures held directly by other game systems are not searched through reflection; if those systems reload them later, the compression patch handles the new load.

## Install for local testing

Copy this directory to `LocalMods/BCnTextureCompression`, enable C# scripting in LuaCsForBarotrauma, enable the content package, and restart the client. Progress and failures are written through `LuaCsLogger`.

## Bundled dependencies

- `BCnEncoder.dll`: BCnEncoder.NET 2.3.0, netstandard2.1 build.
- `CommunityToolkit.HighPerformance.dll`: CommunityToolkit.HighPerformance 8.4.0, net8.0 build.

See `Licenses/THIRD-PARTY-NOTICES.md` for attribution and license terms.
