using System;
using Barotrauma.Extensions;
using HarmonyLib;
using Microsoft.Xna.Framework;

namespace Barotrauma.LuaCs;

public class SettingsMenuSystem : ISettingsMenuSystem
{
    private GUIFrame _menuFrame;
    private GUIButton _menuOpenButton;
    private readonly Harmony _harmony;
    private static SettingsMenuSystem _systemInstance;
    
    public SettingsMenuSystem()
    {
        _systemInstance = this;
        _harmony = Harmony.CreateAndPatchAll(typeof(SettingsMenuSystem));
    }

    [HarmonyPatch(typeof(SettingsMenu), "CreateModsTab"), HarmonyPostfix]
    private static void SettingsMenu_CreateModsTab_Post(SettingsMenu __instance)
    {
        _systemInstance.CreateSettingsMenu(__instance);
    }

    private void CreateSettingsMenu(SettingsMenu __instance)
    {
        var tabIndex = (SettingsMenu.Tab)Enum.GetValues<SettingsMenu.Tab>().Length;
        var contentFrame = CreateNewContentFrame(tabIndex);
        contentFrame.RectTransform.RelativeSize = Vector2.One;
        
        

        GUIFrame CreateNewContentFrame(SettingsMenu.Tab tab)
        {
            if (__instance.tabContents.TryGetValue(tab, out (GUIButton Button, GUIFrame Content) tabContent))
            {
                return tabContent.Content;
            }

            var contentFr = new GUIFrame(new RectTransform(Vector2.One * 0.95f, __instance.contentFrame.RectTransform, Anchor.Center, Pivot.Center), style: null);
            
            var button = new GUIButton(new RectTransform(Vector2.One, __instance.tabber.RectTransform, Anchor.TopLeft, Pivot.TopLeft, scaleBasis: ScaleBasis.Smallest), "", style: $"SettingsMenuTab.Mods")
            {
                ToolTip = TextManager.Get($"LuaCsForBarotrauma.SettingsMenu.ModSettingsButton"),
                OnClicked = (b, _) =>
                {
                    __instance.SelectTab(tab);
                    return false;
                }
            };
            button.RectTransform.MaxSize = RectTransform.MaxPoint;
            button.Children.ForEach(c => c.RectTransform.MaxSize = RectTransform.MaxPoint);
            
            __instance.tabContents.Add(tab, (button, contentFr));

            return contentFr;
        }
    }

    private void DisposeMenuFrame()
    {
        if (_menuFrame is not null)
        {
            _menuFrame.Parent.RemoveChild(_menuFrame);
            _menuFrame = null;
        }
    }
    
    #region DISPOSAL

    public void Dispose()
    {
        if (!ModUtils.Threading.CheckIfClearAndSetBool(ref _isDisposed))
        {
            return;
        }
        DisposeMenuFrame();
        GC.SuppressFinalize(this);
    }
    private int _isDisposed = 0;
    public bool IsDisposed
    {
        get => ModUtils.Threading.GetBool(ref _isDisposed);
        private set => ModUtils.Threading.SetBool(ref _isDisposed, value);
    }
    public FluentResults.Result Reset()
    {
        throw new NotImplementedException();
    }

    #endregion
}
