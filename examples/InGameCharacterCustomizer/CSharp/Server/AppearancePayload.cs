using Barotrauma;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace InGameCharacterCustomizer;

internal readonly struct AppearancePayload
{
    public readonly ushort CharacterId;
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
        CharacterInfo.HeadInfo head = character.Info.Head;
        return new AppearancePayload(
            character.ID,
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
        int tagCount = message.ReadByte();
        HashSet<Identifier> tags = new HashSet<Identifier>();
        for (int i = 0; i < tagCount; i++)
        {
            tags.Add(message.ReadIdentifier());
        }

        return new AppearancePayload(
            characterId,
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

    public AppearancePayload ApplyValidatedTo(Character character)
    {
        CharacterInfo info = character?.Info;
        if (info?.Head == null) { return this; }

        ImmutableHashSet<Identifier> validatedTags = info.Prefab.Heads.Any(h => h.TagSet.SetEquals(Tags))
            ? Tags
            : info.Head.Preset.TagSet;

        info.RecreateHead(
            validatedTags,
            ClampAttachmentIndex(info, WearableType.Hair, HairIndex),
            ClampAttachmentIndex(info, WearableType.Beard, BeardIndex),
            ClampAttachmentIndex(info, WearableType.Moustache, MoustacheIndex),
            ClampAttachmentIndex(info, WearableType.FaceAttachment, FaceAttachmentIndex));

        info.Head.SkinColor = ValidateColor(SkinColor, info.SkinColors.Select(c => c.Color), info.Head.SkinColor);
        info.Head.HairColor = ValidateColor(HairColor, info.HairColors.Select(c => c.Color), info.Head.HairColor);
        info.Head.FacialHairColor = ValidateColor(FacialHairColor, info.FacialHairColors.Select(c => c.Color), info.Head.FacialHairColor);
        info.RefreshHead();

        return FromCharacter(character);
    }

    private static int ClampAttachmentIndex(CharacterInfo info, WearableType wearableType, int index)
    {
        int count = info.CountValidAttachmentsOfType(wearableType);
        if (count <= 0) { return 0; }
        return Math.Max(0, Math.Min(index, count));
    }

    private static Color ValidateColor(Color color, IEnumerable<Color> supportedColors, Color fallback)
    {
        return supportedColors.Contains(color) ? color : fallback;
    }
}
