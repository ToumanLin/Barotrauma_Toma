using Barotrauma;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace InGameCharacterCustomizer;

internal readonly struct AppearancePayload
{
    private static readonly Type PersonalityTraitType = typeof(CharacterInfo).Assembly.GetType("Barotrauma.NPCPersonalityTrait");
    private static readonly PropertyInfo PersonalityTraitProperty = typeof(CharacterInfo).GetProperty("PersonalityTrait", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo PersonalityTraitsField = PersonalityTraitType?.GetField("Traits", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly FieldInfo PersonalityTraitIdentifierField = typeof(Prefab).GetField("Identifier", BindingFlags.Instance | BindingFlags.Public);

    public readonly ushort CharacterId;
    public readonly string Name;
    public readonly ImmutableHashSet<Identifier> Tags;
    public readonly Identifier PersonalityTraitIdentifier;
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
        Identifier personalityTraitIdentifier,
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
        PersonalityTraitIdentifier = personalityTraitIdentifier;
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
            character.Info.Name,
            head.Preset.TagSet,
            GetPersonalityTraitIdentifier(character.Info),
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
        Identifier personalityTraitIdentifier = message.ReadIdentifier();

        return new AppearancePayload(
            characterId,
            name,
            tags.ToImmutableHashSet(),
            personalityTraitIdentifier,
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
        message.WriteIdentifier(PersonalityTraitIdentifier);
        message.WriteByte((byte)HairIndex);
        message.WriteByte((byte)BeardIndex);
        message.WriteByte((byte)MoustacheIndex);
        message.WriteByte((byte)FaceAttachmentIndex);
        message.WriteColorR8G8B8(SkinColor);
        message.WriteColorR8G8B8(HairColor);
        message.WriteColorR8G8B8(FacialHairColor);
    }

    public AppearancePayload WithName(string name)
    {
        return new AppearancePayload(
            CharacterId,
            name,
            Tags,
            PersonalityTraitIdentifier,
            HairIndex,
            BeardIndex,
            MoustacheIndex,
            FaceAttachmentIndex,
            SkinColor,
            HairColor,
            FacialHairColor);
    }

    public AppearancePayload ApplyValidatedTo(Character character)
    {
        CharacterInfo info = character?.Info;
        if (info?.Head == null) { return this; }

        AppearancePayload validated = ValidateFor(info);

        info.Rename(validated.Name);
        SetPersonalityTrait(info, validated.PersonalityTraitIdentifier);
        info.RecreateHead(
            validated.Tags,
            validated.HairIndex,
            validated.BeardIndex,
            validated.MoustacheIndex,
            validated.FaceAttachmentIndex);

        info.Head.SkinColor = validated.SkinColor;
        info.Head.HairColor = validated.HairColor;
        info.Head.FacialHairColor = validated.FacialHairColor;
        info.RefreshHead();
        character.LoadHeadAttachments();

        return FromCharacter(character);
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
            ValidatePersonalityTraitIdentifier(PersonalityTraitIdentifier, info),
            ClampAttachmentIndex(info, WearableType.Hair, HairIndex),
            ClampAttachmentIndex(info, WearableType.Beard, BeardIndex),
            ClampAttachmentIndex(info, WearableType.Moustache, MoustacheIndex),
            ClampAttachmentIndex(info, WearableType.FaceAttachment, FaceAttachmentIndex),
            ValidateColor(SkinColor, info.SkinColors.Select(c => c.Color), info.Head.SkinColor),
            ValidateColor(HairColor, info.HairColors.Select(c => c.Color), info.Head.HairColor),
            ValidateColor(FacialHairColor, info.FacialHairColors.Select(c => c.Color), info.Head.FacialHairColor));
    }

    public void ApplyTo(CharacterInfo info)
    {
        if (info?.Head == null) { return; }

        info.Rename(Name);
        SetPersonalityTrait(info, PersonalityTraitIdentifier);
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
        return Math.Max(0, Math.Min(index, count));
    }

    private static Color ValidateColor(Color color, IEnumerable<Color> supportedColors, Color fallback)
    {
        return new Color(color.R, color.G, color.B, byte.MaxValue);
    }

    private static Identifier GetPersonalityTraitIdentifier(CharacterInfo info)
    {
        object trait = info == null ? null : PersonalityTraitProperty?.GetValue(info);
        return GetTraitIdentifier(trait);
    }

    private static Identifier ValidatePersonalityTraitIdentifier(Identifier requested, CharacterInfo info)
    {
        return !requested.IsEmpty && FindPersonalityTrait(requested) != null
            ? requested
            : GetPersonalityTraitIdentifier(info);
    }

    private static void SetPersonalityTrait(CharacterInfo info, Identifier identifier)
    {
        if (info == null || identifier.IsEmpty || PersonalityTraitProperty == null) { return; }

        object trait = FindPersonalityTrait(identifier);
        if (trait != null)
        {
            PersonalityTraitProperty.SetValue(info, trait);
        }
    }

    private static object FindPersonalityTrait(Identifier identifier)
    {
        if (PersonalityTraitsField?.GetValue(null) is not IEnumerable traits) { return null; }

        foreach (object trait in traits)
        {
            if (GetTraitIdentifier(trait) == identifier) { return trait; }
        }
        return null;
    }

    private static Identifier GetTraitIdentifier(object trait)
    {
        return trait != null && PersonalityTraitIdentifierField != null
            ? (Identifier)PersonalityTraitIdentifierField.GetValue(trait)
            : Identifier.Empty;
    }
}
