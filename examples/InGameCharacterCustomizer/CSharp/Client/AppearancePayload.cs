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
        // Runtime head reloads can lose source-rect scaling used by rectangular custom head sprites.
        HeadSpriteGeometry headSpriteGeometry = HeadSpriteGeometry.Capture(character);

        ApplyTo(character?.Info);
        character?.ReloadHead(
            hairIndex: HairIndex,
            beardIndex: BeardIndex,
            moustacheIndex: MoustacheIndex,
            faceAttachmentIndex: FaceAttachmentIndex);

        headSpriteGeometry?.Restore(character);
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
            conditionalSprites = head.ConditionalSprites
                .Select(s => SpriteGeometry.Capture(s.ActiveSprite))
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

            int count = System.Math.Min(conditionalSprites.Count, head.ConditionalSprites.Count);
            for (int i = 0; i < count; i++)
            {
                conditionalSprites[i].Restore(head.ConditionalSprites[i].ActiveSprite);
            }
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
