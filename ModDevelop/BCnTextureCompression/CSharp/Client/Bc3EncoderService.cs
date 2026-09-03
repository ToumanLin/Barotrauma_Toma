using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using System;

namespace BCnTextureCompression;

internal static class Bc3EncoderService
{
    internal static byte[] Encode(byte[] rgba, int width, int height)
    {
        if (rgba == null) { throw new ArgumentNullException(nameof(rgba)); }
        if (width <= 0 || height <= 0) { throw new ArgumentOutOfRangeException(nameof(width)); }
        if ((width & 3) != 0 || (height & 3) != 0)
        {
            throw new ArgumentException("BC3 dimensions must be multiples of four.");
        }

        int expectedRgbaLength = checked(width * height * 4);
        if (rgba.Length != expectedRgbaLength)
        {
            throw new ArgumentException($"Expected {expectedRgbaLength} RGBA bytes, got {rgba.Length}.", nameof(rgba));
        }

        BcEncoder encoder = new BcEncoder
        {
            OutputOptions =
            {
                Format = CompressionFormat.Bc3,
                Quality = CompressionQuality.Balanced,
                GenerateMipMaps = false
            },
            Options =
            {
                // The game may request compression from a worker already. Keeping the
                // library single-threaded avoids multiplying CPU pressure during migration.
                IsParallel = false,
                TaskCount = 1
            }
        };

        byte[] compressed = encoder.EncodeToRawBytes(
            rgba,
            width,
            height,
            PixelFormat.Rgba32,
            0,
            out int mipWidth,
            out int mipHeight);

        if (mipWidth != width || mipHeight != height)
        {
            throw new InvalidOperationException($"BCnEncoder returned unexpected dimensions {mipWidth}x{mipHeight}.");
        }

        int expectedBc3Length = checked(width * height);
        if (compressed.Length != expectedBc3Length)
        {
            throw new InvalidOperationException($"BCnEncoder returned {compressed.Length} bytes; expected {expectedBc3Length}.");
        }

        return compressed;
    }
}
