using Barotrauma;
using Barotrauma.CharacterEditor;
using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.LuaCs;
using Barotrauma.Steam;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace CharacterViewer;

public sealed partial class CharacterViewerPlugin
{

    public void PreInitPatching()
    {
    }

    public void Initialize()
    {
        instance = this;
        harmony = new Harmony(HarmonyId);

        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "AddToGUIUpdateList", postfix: nameof(AddToGUIUpdateListPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "Update", new[] { typeof(double) }, postfix: nameof(UpdatePostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "CreateFileEditPanel", postfix: nameof(CreateFileEditPanelPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "CreateModesPanel", new[] { typeof(Vector2) }, postfix: nameof(CreateModesPanelPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "CreateMinorModesPanel", new[] { typeof(Vector2) }, postfix: nameof(CreateMinorModesPanelPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "SpawnCharacter", new[] { typeof(Identifier), typeof(RagdollParams) }, prefix: nameof(SpawnCharacterPrefix), postfix: nameof(SpawnCharacterPostfix));
        Patch("Barotrauma.CharacterEditor.CharacterEditorScreen", "DeselectEditorSpecific", postfix: nameof(DeselectEditorPostfix));

        GameMain.ExecuteAfterContentFinishedLoading(SelectCharacterViewerIfRequested);
        LuaCsLogger.Log("CharacterViewer loaded.");
    }

    public void OnLoadCompleted()
    {
    }

    public void Dispose()
    {
        ClearViewerClothing();
        RemoveWindows();
        harmony?.UnpatchSelf();
        harmony = null;

        if (instance == this)
        {
            instance = null;
        }

        LuaCsLogger.Log("CharacterViewer disposed.");
    }

    private void Patch(string typeName, string methodName, Type[] parameterTypes = null, string prefix = null, string postfix = null)
    {
        Type targetType = AccessTools.TypeByName(typeName);
        MethodInfo target = targetType == null ? null : AccessTools.Method(targetType, methodName, parameterTypes);
        if (target == null)
        {
            LuaCsLogger.LogError($"CharacterViewer could not patch {typeName}.{methodName}.");
            return;
        }

        HarmonyMethod prefixMethod = prefix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(CharacterViewerPlugin), prefix));
        HarmonyMethod postfixMethod = postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(CharacterViewerPlugin), postfix));
        harmony.Patch(target, prefixMethod, postfixMethod);
    }

    private static void AddToGUIUpdateListPostfix()
    {
        instance?.AddWindowsToGuiUpdateList();
    }

    private static void SpawnCharacterPrefix()
    {
        instance?.ClearViewerClothing();
    }

    private static void UpdatePostfix(double deltaTime)
    {
        instance?.OnCharacterEditorUpdated((float)deltaTime);
    }

    private static void CreateFileEditPanelPostfix(CharacterEditorScreen __instance)
    {
        instance?.AddModManagerButtonToFilePanel(__instance);
    }

    private static void CreateModesPanelPostfix(CharacterEditorScreen __instance)
    {
        instance?.AddPanelToggleToModesPanel(__instance);
    }

    private static void CreateMinorModesPanelPostfix(CharacterEditorScreen __instance)
    {
        instance?.AddInGameBehaviorToggleToMinorModesPanel(__instance);
    }

    private static void SpawnCharacterPostfix()
    {
        instance?.OnCharacterSpawned();
    }

    private static void DeselectEditorPostfix()
    {
        instance?.RemoveWindows();
    }

    private void SelectCharacterViewerIfRequested()
    {
        if (!GameMain.Instance.ConsoleArguments.Any(arg =>
                arg.Equals("-characterviewer", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-charactereditor", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        GameMain.Instance.Window.Title = "Barotrauma Character Viewer";
        GameMain.CharacterEditorScreen.Select();
        panelsEnabled = true;
        QueueGuiRecreate();
    }
}
