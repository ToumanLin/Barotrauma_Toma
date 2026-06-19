using Barotrauma;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace InGameCharacterCustomizer;

internal readonly struct AppearancePayload
{
    private static readonly FieldInfo TexturePathField = typeof(Limb).GetField("_texturePath", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo DamagedTexturePathField = typeof(Limb).GetField("_damagedTexturePath", BindingFlags.Instance | BindingFlags.NonPublic);

    public readonly ushort CharacterId;
    public readonly string Name;
    public readonly ImmutableHashSet<Identifier> Tags;
    public readonly int HairIndex;
    public readonly int BeardIndex;
    public readonly int MoustacheIndex;
    public readonly int FaceAttachmentIndex;
    public readonly Color SkinColor;
    public readonly Color HairColor;
    public readonly Color FacialHairColor;

    public AppearancePayload(
        ushort characterId,
        string name,
        ImmutableHashSet<Identifier> tags,
        int hairIndex,
        int beardIndex,
        int moustacheIndex,
        int faceAttachmentIndex,
        Color skinColor,
        Color hairColor,
        Color facialHairColor)
    {
        CharacterId = characterId;
        Name = name;
        Tags = tags;
        HairIndex = hairIndex;
        BeardIndex = beardIndex;
        MoustacheIndex = moustacheIndex;
        FaceAttachmentIndex = faceAttachmentIndex;
        SkinColor = skinColor;
        HairColor = hairColor;
        FacialHairColor = facialHairColor;
    }

    public static AppearancePayload FromCharacter(Character character)
    {
        return FromCharacterInfo(character.Info, character.ID);
    }

    public static AppearancePayload FromCharacterInfo(CharacterInfo info, ushort characterId)
    {
        CharacterInfo.HeadInfo head = info.Head;
        return new AppearancePayload(
            characterId,
            info.Name,
            head.Preset.TagSet,
            head.HairIndex,
            head.BeardIndex,
            head.MoustacheIndex,
            head.FaceAttachmentIndex,
            head.SkinColor,
            head.HairColor,
            head.FacialHairColor);
    }

    public static AppearancePayload Read(IReadMessage message)
    {
        ushort characterId = message.ReadUInt16();
        string name = message.ReadString();
        int tagCount = message.ReadByte();
        HashSet<Identifier> tags = new HashSet<Identifier>();
        for (int i = 0; i < tagCount; i++)
        {
            tags.Add(message.ReadIdentifier());
        }

        return new AppearancePayload(
            characterId,
            name,
            tags.ToImmutableHashSet(),
            message.ReadByte(),
            message.ReadByte(),
            message.ReadByte(),
            message.ReadByte(),
            message.ReadColorR8G8B8(),
            message.ReadColorR8G8B8(),
            message.ReadColorR8G8B8());
    }

    public void Write(IWriteMessage message)
    {
        message.WriteUInt16(CharacterId);
        message.WriteString(Name);
        message.WriteByte((byte)System.Math.Min(Tags.Count, byte.MaxValue));
        foreach (Identifier tag in Tags.Take(byte.MaxValue))
        {
            message.WriteIdentifier(tag);
        }
        message.WriteByte((byte)HairIndex);
        message.WriteByte((byte)BeardIndex);
        message.WriteByte((byte)MoustacheIndex);
        message.WriteByte((byte)FaceAttachmentIndex);
        message.WriteColorR8G8B8(SkinColor);
        message.WriteColorR8G8B8(HairColor);
        message.WriteColorR8G8B8(FacialHairColor);
    }

    public AppearancePayload WithCharacterId(ushort characterId)
    {
        return new AppearancePayload(
            characterId,
            Name,
            Tags,
            HairIndex,
            BeardIndex,
            MoustacheIndex,
            FaceAttachmentIndex,
            SkinColor,
            HairColor,
            FacialHairColor);
    }

    public AppearancePayload WithName(string name)
    {
        return new AppearancePayload(
            CharacterId,
            name,
            Tags,
            HairIndex,
            BeardIndex,
            MoustacheIndex,
            FaceAttachmentIndex,
            SkinColor,
            HairColor,
            FacialHairColor);
    }

    public AppearancePayload ValidateFor(CharacterInfo info)
    {
        if (info?.Head == null)
        {
            return this;
        }

        ImmutableHashSet<Identifier> requestedTags = Tags ?? ImmutableHashSet<Identifier>.Empty;
        ImmutableHashSet<Identifier> validatedTags = info.Prefab.Heads.Any(h => h?.TagSet?.SetEquals(requestedTags) == true)
            ? requestedTags
            : info.Head.Preset.TagSet;

        return new AppearancePayload(
            CharacterId,
            Name,
            validatedTags,
            ClampAttachmentIndex(info, WearableType.Hair, HairIndex),
            ClampAttachmentIndex(info, WearableType.Beard, BeardIndex),
            ClampAttachmentIndex(info, WearableType.Moustache, MoustacheIndex),
            ClampAttachmentIndex(info, WearableType.FaceAttachment, FaceAttachmentIndex),
            ValidateColor(SkinColor, info.SkinColors.Select(c => c.Color), info.Head.SkinColor),
            ValidateColor(HairColor, info.HairColors.Select(c => c.Color), info.Head.HairColor),
            ValidateColor(FacialHairColor, info.FacialHairColors.Select(c => c.Color), info.Head.FacialHairColor));
    }

    public void ApplyTo(Character character)
    {
        if (character?.Info?.Head == null || character.AnimController == null) { return; }

        // Runtime head reloads can lose source-rect scaling used by rectangular custom head sprites.
        HeadSpriteGeometry headSpriteGeometry = HeadSpriteGeometry.Capture(character);

        AppearancePayload validated = ValidateFor(character.Info);
        validated.ApplyTo(character.Info);
        character.ReloadHead(
            hairIndex: validated.HairIndex,
            beardIndex: validated.BeardIndex,
            moustacheIndex: validated.MoustacheIndex,
            faceAttachmentIndex: validated.FaceAttachmentIndex);
        foreach (Limb limb in character.AnimController.Limbs)
        {
            RecreateLimbSprites(limb);
        }
        foreach (WearableSprite wearable in character.AnimController.Limbs.SelectMany(l => l.WearingItems).Distinct())
        {
            wearable.Picker = null;
            wearable.Picker = character;
        }

        headSpriteGeometry?.Restore(character);
    }

    private static void RecreateLimbSprites(Limb limb)
    {
        TexturePathField?.SetValue(limb, null);
        DamagedTexturePathField?.SetValue(limb, null);
        limb.RecreateSprites();
    }

    public void ApplyTo(CharacterInfo info)
    {
        if (info?.Head == null) { return; }

        info.Rename(Name);
        info.RecreateHead(Tags, HairIndex, BeardIndex, MoustacheIndex, FaceAttachmentIndex);
        info.Head.SkinColor = SkinColor;
        info.Head.HairColor = HairColor;
        info.Head.FacialHairColor = FacialHairColor;
        info.RefreshHead();
    }

    private static int ClampAttachmentIndex(CharacterInfo info, WearableType wearableType, int index)
    {
        int count = info.CountValidAttachmentsOfType(wearableType);
        if (count <= 0) { return 0; }
        return System.Math.Max(0, System.Math.Min(index, count));
    }

    private static Color ValidateColor(Color color, IEnumerable<Color> supportedColors, Color fallback)
    {
        return supportedColors.Contains(color) ? color : fallback;
    }

    private sealed class HeadSpriteGeometry
    {
        private readonly SpriteGeometry sprite;
        private readonly SpriteGeometry deformSprite;
        private readonly SpriteGeometry damagedSprite;
        private readonly List<SpriteGeometry> conditionalSprites;

        private HeadSpriteGeometry(Limb head)
        {
            sprite = SpriteGeometry.Capture(head.Sprite);
            deformSprite = SpriteGeometry.Capture(head.DeformSprite?.Sprite);
            damagedSprite = SpriteGeometry.Capture(head.DamagedSprite);
            conditionalSprites = head.ConditionalSprites == null
                ? new List<SpriteGeometry>()
                : head.ConditionalSprites
                    .Select(s => SpriteGeometry.Capture(GetActiveSprite(s)))
                    .ToList();
        }

        public static HeadSpriteGeometry Capture(Character character)
        {
            Limb head = character?.AnimController?.GetLimb(LimbType.Head);
            return head == null ? null : new HeadSpriteGeometry(head);
        }

        public void Restore(Character character)
        {
            Limb head = character?.AnimController?.GetLimb(LimbType.Head);
            if (head == null) { return; }

            sprite.Restore(head.Sprite);
            deformSprite.Restore(head.DeformSprite?.Sprite);
            damagedSprite.Restore(head.DamagedSprite);

            if (head.ConditionalSprites == null) { return; }

            int count = System.Math.Min(conditionalSprites.Count, head.ConditionalSprites.Count);
            for (int i = 0; i < count; i++)
            {
                conditionalSprites[i].Restore(GetActiveSprite(head.ConditionalSprites[i]));
            }
        }

        private static Sprite GetActiveSprite(ConditionalSprite conditionalSprite)
        {
            return conditionalSprite?.Sprite ?? conditionalSprite?.DeformableSprite?.Sprite;
        }
    }

    private sealed class SpriteGeometry
    {
        private readonly Rectangle sourceRect;
        private readonly Vector2 origin;
        private readonly Vector2 size;
        private readonly bool hasValue;

        private SpriteGeometry(Sprite sprite)
        {
            if (sprite == null) { return; }

            sourceRect = sprite.SourceRect;
            origin = sprite.Origin;
            size = sprite.size;
            hasValue = true;
        }

        public static SpriteGeometry Capture(Sprite sprite)
        {
            return new SpriteGeometry(sprite);
        }

        public void Restore(Sprite sprite)
        {
            if (!hasValue || sprite == null) { return; }

            sprite.SourceRect = sourceRect;
            sprite.Origin = origin;
            sprite.size = size;
        }
    }
}
