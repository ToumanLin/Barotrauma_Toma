using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma.LuaCs;

internal sealed class ModsGameplaySettingsMenu : ModsSettingsMenu
{
    public ModsGameplaySettingsMenu(GUIFrame contentFrame, 
        IPackageManagementService packageManagementService, 
        IConfigService configService, 
        SettingsMenu settingsMenuInstance) : base(contentFrame, packageManagementService, configService, settingsMenuInstance)
    {
        var mainLayoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 1f), contentFrame.RectTransform, Anchor.Center), false, Anchor.TopLeft);
        // page title
        var menuTitleLayoutGroup = new GUILayoutGroup(
                new RectTransform(new Vector2(1f, 0.06f), mainLayoutGroup.RectTransform, Anchor.TopLeft), true, Anchor.TopLeft);
        GUIUtil.Label(menuTitleLayoutGroup, "Mods Gameplay Settings", GUIStyle.LargeFont, new Vector2(1f, 1f));
        
        // page contents
        var contentAreaLayoutGroup = new GUILayoutGroup(
                new RectTransform(new Vector2(1f, 0.94f), mainLayoutGroup.RectTransform, Anchor.BottomLeft), false,
                Anchor.TopLeft);
        
        var searchBarLayoutGroup = new GUILayoutGroup(
            new RectTransform(new Vector2(1f, 0.06f), contentAreaLayoutGroup.RectTransform, Anchor.TopCenter), true, Anchor.CenterLeft);
        GUIUtil.Label(searchBarLayoutGroup, "Search: ", GUIStyle.SubHeadingFont, new Vector2(0.1f, 1f));
        var searchBar = new GUITextBox(
            new RectTransform(new Vector2(0.85f, 0.1f), searchBarLayoutGroup.RectTransform, Anchor.TopLeft),
            createClearButton: true)
        {
            OnTextChangedDelegate = (btn, txt) =>
            {
                // TODO: Execute filter here
                return true;
            }
        };
        // main display area
        var settingsContentAreaGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.90f), contentAreaLayoutGroup.RectTransform, Anchor.BottomCenter));
        GUIUtil.Spacer(settingsContentAreaGroup, Vector2.One);
        var (modCategoryDisplayGroup, settingsDisplayGroup) = GUIUtil.CreateSidebars(settingsContentAreaGroup, true);
        modCategoryDisplayGroup.RectTransform.RelativeSize = new Vector2(0.3f, 1f);
        settingsDisplayGroup.RectTransform.RelativeSize = new Vector2(0.7f, 1f);

        // Mods & Category Selectors
        var cpList = packageManagementService.GetAllLoadedPackages().OrderBy(cp => cp.Name == "Vanilla" ? 0 : 1).ThenBy(cp => cp.Name).ToList();
        var modSelectDropDown = GUIUtil.Dropdown<ContentPackage>(modCategoryDisplayGroup, cp => cp.Name == "Vanilla" ? "All" : cp.Name, null, cpList, cpList[0], cp =>
        {
            // TODO: filter selections by adding it to the search bar
        }, Vector2.One, 2);


        void GenerateDisplayFromFilter(string text)
        {

        }

        void GenerateCategoryListDisplay(GUILayoutGroup layoutGroup)
        {

        }

        void GenerateSettingsListDisplay(GUILayoutGroup layoutGroup) 
        {

        }
    }


    protected override void DisposeInternal()
    {
        // TODO: Finish this later.
    }
}
