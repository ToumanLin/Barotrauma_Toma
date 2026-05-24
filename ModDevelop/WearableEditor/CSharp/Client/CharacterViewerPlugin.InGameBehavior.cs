using Barotrauma;
using Barotrauma.CharacterEditor;
using Barotrauma.LuaCs;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CharacterViewer;

public sealed partial class CharacterViewerPlugin
{
    private const string InGameBehaviorToggleUserData = "CharacterViewer.InGameBehaviorToggle";

    private static readonly FieldInfo editorFrozenField = AccessTools.Field(typeof(CharacterEditorScreen), "isFrozen");
    private static readonly MethodInfo statusEffectUpdateAllMethod = AccessTools.Method(AccessTools.TypeByName("Barotrauma.StatusEffect"), "UpdateAll", new[] { typeof(float) });

    private bool inGameBehaviorEnabled;

    private void AddInGameBehaviorToggleToMinorModesPanel(CharacterEditorScreen editor)
    {
        GUIFrame minorModesPanel = AccessTools.Field(editor.GetType(), "minorModesPanel")?.GetValue(editor) as GUIFrame;
        GUILayoutGroup layout = minorModesPanel?.GetChild<GUILayoutGroup>();
        if (layout == null) { return; }
        if (layout.GetAllChildren().Any(c => c.UserData as string == InGameBehaviorToggleUserData))
        {
            SyncInGameBehaviorToggle();
            return;
        }

        var tickBox = new GUITickBox(new RectTransform(new Vector2(1.0f, 0.03f), layout.RectTransform), Text("toggle.ingamebehavior", "IN-GAME BEHAVIOR [H]"))
        {
            UserData = InGameBehaviorToggleUserData,
            Selected = inGameBehaviorEnabled,
            OnSelected = box =>
            {
                SetInGameBehaviorEnabled(box.Selected);
                return true;
            }
        };

        minorModesPanel.RectTransform.MinSize += new Point(0, tickBox.RectTransform.MinSize.Y + layout.AbsoluteSpacing);
        layout.Recalculate();
    }

    private void SetInGameBehaviorEnabled(bool enabled)
    {
        inGameBehaviorEnabled = enabled;
        SyncInGameBehaviorToggle();
    }

    private void SyncInGameBehaviorToggle()
    {
        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        if (editor == null) { return; }

        GUIFrame minorModesPanel = AccessTools.Field(editor.GetType(), "minorModesPanel")?.GetValue(editor) as GUIFrame;
        var tickBox = minorModesPanel?.GetAllChildren().OfType<GUITickBox>().FirstOrDefault(c => c.UserData as string == InGameBehaviorToggleUserData);
        if (tickBox != null)
        {
            tickBox.Selected = inGameBehaviorEnabled;
        }
    }

    private void UpdateInGameBehavior(float deltaTime)
    {
        if (!inGameBehaviorEnabled) { return; }
        if (deltaTime <= 0.0f) { return; }
        if (IsBlockingGameMenuOpen()) { return; }

        CharacterEditorScreen editor = GameMain.CharacterEditorScreen;
        if (editor == null || IsCharacterEditorFrozen(editor)) { return; }

        Character character = CurrentCharacter;
        if (character == null || character.Removed || character.CharacterHealth == null || character.AnimController == null) { return; }

        bool previousInWater = character.AnimController.InWater;
        bool previousHeadInWater = character.AnimController.HeadInWater;
        try
        {
            SetRagdollWaterState(character.AnimController, inWater: false, headInWater: false);
            character.ApplyStatusEffects(ActionType.Always, deltaTime);
            character.ApplyStatusEffects(ActionType.NotInWater, deltaTime);
            character.ApplyStatusEffects(ActionType.OnActive, deltaTime);
            UpdateCharacterInventoryItems(character, deltaTime, editor.Cam);
            UpdateGlobalStatusEffects(deltaTime);
            character.CharacterHealth.Update(deltaTime);
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError($"CharacterViewer failed to update in-game behavior: {ex}");
        }
        finally
        {
            SetRagdollWaterState(character.AnimController, previousInWater, previousHeadInWater);
        }
    }

    private static bool IsCharacterEditorFrozen(CharacterEditorScreen editor)
    {
        return editorFrozenField?.GetValue(editor) is true;
    }

    private static void SetRagdollWaterState(object ragdoll, bool inWater, bool headInWater)
    {
        if (ragdoll == null) { return; }
        Type ragdollType = ragdoll.GetType();
        AccessTools.Field(ragdollType, "inWater")?.SetValue(ragdoll, inWater);
        AccessTools.Field(ragdollType, "headInWater")?.SetValue(ragdoll, headInWater);
    }

    private static void UpdateCharacterInventoryItems(Character character, float deltaTime, Camera camera)
    {
        IEnumerable<Item> items = character.Inventory?.AllItems;
        if (items == null) { return; }

        foreach (Item item in items.Where(static item => item != null).Distinct().ToList())
        {
            if (item.Removed || item.IsInRemoveQueue) { continue; }
            try
            {
                item.Update(deltaTime, camera);
            }
            catch (Exception ex)
            {
                LuaCsLogger.LogError($"CharacterViewer failed to update inventory item \"{item.Name}\": {ex}");
            }
        }
    }

    private static void UpdateGlobalStatusEffects(float deltaTime)
    {
        try
        {
            statusEffectUpdateAllMethod?.Invoke(null, new object[] { deltaTime });
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError($"CharacterViewer failed to update global status effects: {ex}");
        }
    }
}
