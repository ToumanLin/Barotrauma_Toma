using Barotrauma;
using System;
using System.Threading;

namespace BCnTextureCompression;

internal static class TextureCompressionPatch
{
    private static int failureLogged;

    internal static bool CompressDxt5Prefix(byte[] data, int width, int height, ref byte[] __result)
    {
        try
        {
            __result = Bc3EncoderService.Encode(data, width, height);
            return false;
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref failureLogged, 1) == 0)
            {
                LuaCsLogger.LogError($"BCnTextureCompression failed to encode a texture; this and subsequent failed calls fall back to the original encoder. {ex}");
            }
            return true;
        }
    }
}
