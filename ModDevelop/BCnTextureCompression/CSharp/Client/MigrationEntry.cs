using Microsoft.Xna.Framework.Graphics;

namespace BCnTextureCompression;

internal sealed class MigrationEntry
{
    internal MigrationEntry(string sourcePath, Texture2D oldTexture)
    {
        SourcePath = sourcePath;
        OldTexture = oldTexture;
        Width = oldTexture.Width;
        Height = oldTexture.Height;
    }

    internal string SourcePath { get; }
    internal Texture2D OldTexture { get; }
    internal int Width { get; }
    internal int Height { get; }
}
