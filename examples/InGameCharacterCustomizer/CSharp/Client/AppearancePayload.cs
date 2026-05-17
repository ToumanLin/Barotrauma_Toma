using Barotrauma;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
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

    public AppearancePayload WithCharacterId(ushort characterId)
    {
        return new AppearancePayload(
            characterId,
            Tags,
            HairIndex,
            BeardIndex,
            MoustacheIndex,
            FaceAttachmentIndex,
            SkinColor,
            HairColor,
            FacialHairColor);
    }

    public void ApplyTo(Character character)
    {
        ApplyTo(character?.Info);
        character?.ReloadHead(
            hairIndex: HairIndex,
            beardIndex: BeardIndex,
            moustacheIndex: MoustacheIndex,
            faceAttachmentIndex: FaceAttachmentIndex);
    }

    public void ApplyTo(CharacterInfo info)
    {
        if (info?.Head == null) { return; }

        info.RecreateHead(Tags, HairIndex, BeardIndex, MoustacheIndex, FaceAttachmentIndex);
        info.Head.SkinColor = SkinColor;
        info.Head.HairColor = HairColor;
        info.Head.FacialHairColor = FacialHairColor;
        info.RefreshHead();
    }
}
