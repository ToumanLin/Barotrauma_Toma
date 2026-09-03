using Barotrauma;
using Barotrauma.LuaCs;
using HarmonyLib;
using System;
using System.Reflection;

namespace BCnTextureCompression;

public sealed class BCnTextureCompressionPlugin : IAssemblyPlugin
{
    private const string HarmonyId = "BCnTextureCompression.Client";

    private Harmony harmony;
    private SpriteTextureMigrationService migrationService;
    private bool patchInstalled;

    public void PreInitPatching()
    {
    }

    public void Initialize()
    {
        harmony = new Harmony(HarmonyId);
        patchInstalled = InstallCompressionPatch();

        if (patchInstalled)
        {
            LuaCsLogger.Log("BCnTextureCompression is handling future DXT5/BC3 compression requests.");
        }
    }

    public void OnLoadCompleted()
    {
        migrationService = new SpriteTextureMigrationService();
        migrationService.Start();
    }

    public void Dispose()
    {
        migrationService?.Dispose();
        migrationService = null;

        harmony?.UnpatchSelf();
        harmony = null;
        patchInstalled = false;

        LuaCsLogger.Log("BCnTextureCompression disposed.");
    }

    private bool InstallCompressionPatch()
    {
        MethodInfo target = AccessTools.Method(typeof(TextureLoader), "CompressDxt5", new[]
        {
            typeof(byte[]),
            typeof(int),
            typeof(int)
        });
        MethodInfo prefix = AccessTools.Method(typeof(TextureCompressionPatch), nameof(TextureCompressionPatch.CompressDxt5Prefix));

        if (target == null || prefix == null)
        {
            LuaCsLogger.LogError("BCnTextureCompression could not find TextureLoader.CompressDxt5; future texture loads will use the original encoder.");
            return false;
        }

        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            return true;
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError($"BCnTextureCompression failed to patch TextureLoader.CompressDxt5: {ex}");
            return false;
        }
    }
}
