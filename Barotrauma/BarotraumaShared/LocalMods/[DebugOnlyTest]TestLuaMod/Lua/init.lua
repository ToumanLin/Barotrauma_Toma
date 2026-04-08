print("Hello!")

Hook.Add("character.created", "test", function(character) 
    print("character.created: ", character)
end)

Hook.Add("character.death", "test", function(character) 
    print("character.death: ", character)
end)

Hook.Add("character.giveJobItems", "test", function(character) 
    print("character.giveJobItems: ", character)
end)

Hook.Add("roundStart", "test", function()
    print("roundStart")
end)

Hook.Add("roundEnd", "test", function()
    print("roundEnd")
end)

Hook.Add("missionsEnded", "test", function()
    print("missionsEnded")
end)

local failed, package = trygetpackage("[DebugOnlyTest]TestLuaMod")

local success, config = ConfigService.TryGetConfig(SettingBase.Single, package, "TestFloat")

print("config ", success, " ", config)

local lastTime = 0
Hook.Add("think", "printconfig", function()
    if lastTime > Timer.Time then return end

    lastTime = Timer.Time + 10
    print(config.Value)

    if SERVER then
        config.TrySetValue(config.Value + 1)
    end
end)
