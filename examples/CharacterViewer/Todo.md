Feature Request: Further develop the editor for sprite editing:
1. The left parameter GUI should only list one sprite entry at a time
    1.1. Make a new panel "Wearable Sprite List" that list all the sprite entries of the equiped clothing, the panel should be triggle by key [7] as well
        1.1.1. Panel List entry template:
            - [Sprite Name (can be blank)], [BodyPartName], [sourcerect(4 num)]
        1.1.2. Highlighted when the sprite is selected to edit
        1.1.3. Also highlight the sourcerect in the "Clothing sprite" panel
    1.2. User can select sprite entry to edit from the "Wearable Sprite List" panel, then the left parameter GUI will show the parameter of the selected sprite entry
    1.3. Use can also select sprite entry to edit from "Clothing sprite" panel by click the highlighted sourcerect.
2. Sprite entry parameter GUI should have the following feature
    2.1. Use style of vanilla game editor style
    2.2. The editor should handle editing the following parameter: 
        texture
        name
        sourecerect
        origin
        size
        depth
        compress
        limb
        hidelimb
        hideotherwearables
        alhpaclipotherwearables
        canbehiddenbyotherwearables
        hidewearablesoftype
        inheritlimbdepth
        depthlimb
        inheritscale
        ignorelimbscale
        ignoretexturescale
        inheritorigin
        inheritsourcerect
        scale
        rotation
        sound
    (you can find their description, Type, and Default value from C:\Users\Touma\Documents\GitHub\LuaCsForBarotrauma\docs\wearable_sprite_properties.md )
    2.3. (Do not implement it yet, but leave a placeholder for it) The sprite elements inside wearable can have 2 kinds of nested child nodes, they are "LightComponent" and "override", the editor should find a way such that it allow user to navigate and edit the nested child nodes, you can read details in C:\Users\Touma\Documents\GitHub\LuaCsForBarotrauma\docs\wearable_sprite_properties.md
    2.4. append the XML code box to the end of all entries, make sure the box visualize xml with indentation and line breaks, and dynamic allocate box height based on the size of the xml, and put the [save] and [revert] buttons in the next row.

Goal: Add support for Huskappendage editing
The huskappendage is a character configuration feature originally created by the Barotrauma developers to handle the Husk Infection (which sprouts a tentacle from the mouth) and other genetic mutations (like Mudraptor beaks or Spineling spikes).
huskappendage allows the game to load a completely new structural XML file. This XML can contain new <limb> definitions, new <joint> connections, unique physics properties, and separate light sources.
Since A standard <Wearable> item can only paste a flat sprite on top of the default human skeleton. The use of <HuskAppendage> makes more dynamic sprite possible.

There is a small demo for you to understand how mod authors use <HuskAppendage>, you can search Wf_New_XDS01_Engine as the keyword : C:\Users\Touma\Documents\GitHub\LuaCsForBarotrauma\examples\HuskappendageExample

1. 