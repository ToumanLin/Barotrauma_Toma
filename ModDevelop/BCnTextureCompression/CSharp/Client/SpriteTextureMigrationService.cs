using Barotrauma;
using Barotrauma.LuaCs;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace BCnTextureCompression;

internal sealed class SpriteTextureMigrationService : IDisposable
{
    private sealed class TextureReferenceComparer : IEqualityComparer<Texture2D>
    {
        internal static readonly TextureReferenceComparer Instance = new TextureReferenceComparer();

        public bool Equals(Texture2D x, Texture2D y) => ReferenceEquals(x, y);

        public int GetHashCode(Texture2D obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private readonly PropertyInfo spriteTextureProperty;
    private readonly FieldInfo spriteListField;
    private readonly FieldInfo textureRefCountsField;
    private readonly FieldInfo textureLoaderGraphicsDeviceField;
    private readonly FieldInfo counterTextureField;
    private readonly PropertyInfo counterEntryValueProperty;
    private readonly object spriteListLock;
    private readonly CancellationTokenSource cancellation = new CancellationTokenSource();

    private Thread worker;
    private int active = 1;
    private int generation;

    internal SpriteTextureMigrationService()
    {
        spriteTextureProperty = AccessTools.Property(typeof(Sprite), "texture");
        spriteListField = AccessTools.Field(typeof(Sprite), "list");
        textureRefCountsField = AccessTools.Field(typeof(Sprite), "textureRefCounts");
        textureLoaderGraphicsDeviceField = AccessTools.Field(typeof(TextureLoader), "_graphicsDevice");

        Type dictionaryType = textureRefCountsField?.FieldType;
        Type counterType = dictionaryType?.GetGenericArguments().ElementAtOrDefault(1);
        Type entryType = dictionaryType == null ? null : typeof(KeyValuePair<,>).MakeGenericType(dictionaryType.GetGenericArguments());

        counterTextureField = counterType == null ? null : AccessTools.Field(counterType, "Texture");
        counterEntryValueProperty = entryType?.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        spriteListLock = spriteListField?.GetValue(null);
    }

    private bool ReflectionAvailable =>
        spriteTextureProperty != null &&
        spriteListField != null &&
        textureRefCountsField != null &&
        textureLoaderGraphicsDeviceField != null &&
        counterTextureField != null &&
        counterEntryValueProperty != null &&
        spriteListLock != null;

    internal void Start()
    {
        if (!ReflectionAvailable)
        {
            LuaCsLogger.LogError($"BCnTextureCompression could not access the Sprite texture cache ({GetMissingReflectionMembers()}); already-loaded textures will not be migrated.");
            return;
        }

        List<MigrationEntry> entries;
        try
        {
            entries = CaptureEntries();
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError($"BCnTextureCompression failed to inspect the Sprite texture cache: {ex}");
            return;
        }

        if (entries.Count == 0)
        {
            LuaCsLogger.Log("BCnTextureCompression found no already-loaded Sprite textures that require migration.");
            return;
        }

        int workerGeneration = Interlocked.Increment(ref generation);
        worker = new Thread(() => RunMigration(entries, workerGeneration))
        {
            IsBackground = true,
            Name = "BCnTextureCompression migration"
        };
        worker.Start();
        LuaCsLogger.Log($"BCnTextureCompression queued {entries.Count} already-loaded Sprite texture(s) for background migration.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref active, 0) == 0) { return; }

        cancellation.Cancel();
        Interlocked.Increment(ref generation);

        // Do not join here. A worker can be waiting for a queued main-thread commit,
        // and joining from the main thread would deadlock the game.
        worker = null;
    }

    private List<MigrationEntry> CaptureEntries()
    {
        Dictionary<Texture2D, MigrationEntry> uniqueEntries =
            new Dictionary<Texture2D, MigrationEntry>(TextureReferenceComparer.Instance);

        lock (spriteListLock)
        {
            foreach (Sprite sprite in Sprite.LoadedSprites)
            {
                Texture2D oldTexture = GetSpriteTexture(sprite);
                if (oldTexture == null || oldTexture.IsDisposed || oldTexture.Format != SurfaceFormat.Dxt5) { continue; }
                if (!sprite.Compress || (oldTexture.Width & 3) != 0 || (oldTexture.Height & 3) != 0) { continue; }
                if (uniqueEntries.ContainsKey(oldTexture)) { continue; }

                string sourcePath;
                try
                {
                    sourcePath = sprite.FullPath;
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) { continue; }
                uniqueEntries.Add(oldTexture, new MigrationEntry(sourcePath, oldTexture));
            }
        }

        return uniqueEntries.Values.ToList();
    }

    private void RunMigration(IReadOnlyList<MigrationEntry> entries, int workerGeneration)
    {
        int migrated = 0;
        int skipped = 0;
        int failed = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            if (ShouldStop(workerGeneration)) { break; }

            MigrationEntry entry = entries[i];
            try
            {
                byte[] rgba;
                int width;
                int height;
                using (FileStream stream = File.OpenRead(entry.SourcePath))
                {
                    rgba = Texture2D.TextureDataFromStream(stream, out width, out height, out int _);
                }

                if (width != entry.Width || height != entry.Height)
                {
                    skipped++;
                    continue;
                }

                byte[] compressed = Bc3EncoderService.Encode(rgba, width, height);
                rgba = null;

                if (ShouldStop(workerGeneration)) { break; }

                bool committed = false;
                CrossThread.RequestExecutionOnMainThread(() =>
                {
                    committed = Commit(entry, compressed, workerGeneration);
                });

                if (committed) { migrated++; }
                else { skipped++; }
            }
            catch (Exception ex)
            {
                failed++;
                if (failed <= 5)
                {
                    LuaCsLogger.LogError($"BCnTextureCompression failed to migrate \"{entry.SourcePath}\": {ex.Message}");
                }
                else if (failed == 6)
                {
                    LuaCsLogger.LogError("BCnTextureCompression is suppressing further per-texture migration errors; the final summary will include the failure count.");
                }
            }

            int processed = i + 1;
            if (processed % 25 == 0 && processed < entries.Count)
            {
                LuaCsLogger.Log($"BCnTextureCompression migration progress: {processed}/{entries.Count} processed, {migrated} replaced, {skipped} skipped, {failed} failed.");
            }
        }

        if (!ShouldStop(workerGeneration))
        {
            LuaCsLogger.Log($"BCnTextureCompression migration finished: {migrated} replaced, {skipped} skipped, {failed} failed.");
        }
    }

    private bool Commit(MigrationEntry entry, byte[] compressed, int workerGeneration)
    {
        if (ShouldStop(workerGeneration)) { return false; }

        Texture2D oldTexture = entry.OldTexture;
        if (oldTexture == null || oldTexture.IsDisposed || oldTexture.Width != entry.Width || oldTexture.Height != entry.Height)
        {
            return false;
        }

        if (!IsTextureStillReferenced(oldTexture)) { return false; }

        Texture2D newTexture = null;
        try
        {
            GraphicsDevice graphicsDevice = textureLoaderGraphicsDeviceField.GetValue(null) as GraphicsDevice;
            if (graphicsDevice == null || graphicsDevice.IsDisposed) { return false; }

            newTexture = new Texture2D(
                graphicsDevice,
                entry.Width,
                entry.Height,
                mipmap: false,
                format: SurfaceFormat.Dxt5);
            newTexture.SetData(compressed);

            lock (spriteListLock)
            {
                if (ShouldStop(workerGeneration) || oldTexture.IsDisposed)
                {
                    newTexture.Dispose();
                    return false;
                }

                List<object> countersToReplace = GetTextureCounters()
                    .Where(counter => ReferenceEquals(counterTextureField.GetValue(counter), oldTexture))
                    .ToList();
                List<Sprite> spritesToReplace = Sprite.LoadedSprites
                    .Where(sprite => ReferenceEquals(GetSpriteTexture(sprite), oldTexture))
                    .ToList();

                if (spritesToReplace.Count == 0 && countersToReplace.Count == 0)
                {
                    newTexture.Dispose();
                    return false;
                }

                try
                {
                    foreach (object counter in countersToReplace)
                    {
                        counterTextureField.SetValue(counter, newTexture);
                    }
                    foreach (Sprite sprite in spritesToReplace)
                    {
                        spriteTextureProperty.SetValue(sprite, newTexture);
                    }
                }
                catch
                {
                    foreach (object counter in countersToReplace)
                    {
                        if (ReferenceEquals(counterTextureField.GetValue(counter), newTexture))
                        {
                            counterTextureField.SetValue(counter, oldTexture);
                        }
                    }
                    foreach (Sprite sprite in spritesToReplace)
                    {
                        if (ReferenceEquals(GetSpriteTexture(sprite), newTexture))
                        {
                            spriteTextureProperty.SetValue(sprite, oldTexture);
                        }
                    }
                    throw;
                }
            }

            try
            {
                oldTexture.Dispose();
            }
            catch (Exception ex)
            {
                // The swap is already complete, so keep the new texture live even if
                // the graphics backend fails to release the old resource immediately.
                LuaCsLogger.LogError($"BCnTextureCompression replaced \"{entry.SourcePath}\" but could not dispose its old GPU texture: {ex.Message}");
            }
            return true;
        }
        catch
        {
            newTexture?.Dispose();
            throw;
        }
    }

    private bool IsTextureStillReferenced(Texture2D oldTexture)
    {
        lock (spriteListLock)
        {
            if (GetTextureCounters().Any(counter => ReferenceEquals(counterTextureField.GetValue(counter), oldTexture)))
            {
                return true;
            }

            return Sprite.LoadedSprites.Any(sprite => ReferenceEquals(GetSpriteTexture(sprite), oldTexture));
        }
    }

    private IEnumerable<object> GetTextureCounters()
    {
        object dictionary = textureRefCountsField.GetValue(null);
        if (dictionary is not IEnumerable entries) { yield break; }

        foreach (object entry in entries)
        {
            object counter = counterEntryValueProperty.GetValue(entry);
            if (counter != null) { yield return counter; }
        }
    }

    private Texture2D GetSpriteTexture(Sprite sprite)
    {
        return spriteTextureProperty.GetValue(sprite) as Texture2D;
    }

    private string GetMissingReflectionMembers()
    {
        List<string> missing = new List<string>();
        if (spriteTextureProperty == null) { missing.Add("Sprite.texture property"); }
        if (spriteListField == null) { missing.Add("Sprite.list field"); }
        if (textureRefCountsField == null) { missing.Add("Sprite.textureRefCounts field"); }
        if (textureLoaderGraphicsDeviceField == null) { missing.Add("TextureLoader._graphicsDevice field"); }
        if (counterTextureField == null) { missing.Add("TextureRefCounter.Texture field"); }
        if (counterEntryValueProperty == null) { missing.Add("cache entry Value property"); }
        if (spriteListLock == null) { missing.Add("Sprite.list value"); }
        return missing.Count == 0 ? "unknown reflection error" : string.Join(", ", missing);
    }

    private bool ShouldStop(int workerGeneration)
    {
        return Volatile.Read(ref active) == 0 ||
               cancellation.IsCancellationRequested ||
               Volatile.Read(ref generation) != workerGeneration;
    }
}
