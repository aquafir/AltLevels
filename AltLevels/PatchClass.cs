using ACE.Database.Models.Auth;
using ACE.Server.WorldObjects.Entity;
using ACE.Server.WorldObjects;

namespace AltLevels;

[HarmonyPatch]
public class PatchClass
{
    #region Settings
    const int RETRIES = 10;

    public static Settings Settings = new();
    static string settingsPath => Path.Combine(Mod.ModPath, "Settings.json");
    private FileInfo settingsInfo = new(settingsPath);

    private JsonSerializerOptions _serializeOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private void SaveSettings()
    {
        string jsonString = JsonSerializer.Serialize(Settings, _serializeOptions);

        if (!settingsInfo.RetryWrite(jsonString, RETRIES))
        {
            ModManager.Log($"Failed to save settings to {settingsPath}...", ModManager.LogLevel.Warn);
            Mod.State = ModState.Error;
        }
    }

    private void LoadSettings()
    {
        if (!settingsInfo.Exists)
        {
            ModManager.Log($"Creating {settingsInfo}...");
            SaveSettings();
        }
        else
            ModManager.Log($"Loading settings from {settingsPath}...");

        if (!settingsInfo.RetryRead(out string jsonString, RETRIES))
        {
            Mod.State = ModState.Error;
            return;
        }

        try
        {
            Settings = JsonSerializer.Deserialize<Settings>(jsonString, _serializeOptions);
        }
        catch (Exception)
        {
            ModManager.Log($"Failed to deserialize Settings: {settingsPath}", ModManager.LogLevel.Warn);
            Mod.State = ModState.Error;
            return;
        }
    }
    #endregion

    #region Start/Shutdown
    public void Start()
    {
        //Need to decide on async use
        Mod.State = ModState.Loading;
        LoadSettings();

        if (Mod.State == ModState.Error)
        {
            ModManager.DisableModByPath(Mod.ModPath);
            return;
        }

        Mod.State = ModState.Running;
    }

    public void Shutdown()
    {
        //if (Mod.State == ModState.Running)
        // Shut down enabled mod...

        //If the mod is making changes that need to be saved use this and only manually edit settings when the patch is not active.
        //SaveSettings();

        if (Mod.State == ModState.Error)
            ModManager.Log($"Improper shutdown: {Mod.ModPath}", ModManager.LogLevel.Error);
    }
    #endregion

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.HandleActionRaiseSkill), new Type[] { typeof(Skill), typeof(uint) })]
    public static bool PreHandleActionRaiseSkill(Skill skill, uint amount, ref Player __instance, ref bool __result)
    {
        //Pretend amount is the number of levels
        amount = amount > 300 ? 10 : 1u;

        for (var i = 0; i < amount; i++)
            if (!__instance.TryRaiseSkill(skill))
                break;

        //Try to always update?
        __instance.Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(__instance, __instance.GetCreatureSkill(skill, false)));

        //Skip original handling
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CreatureSkill), nameof(CreatureSkill.InitLevel), MethodType.Getter)]
    public static void PostGetInitLevel(ref CreatureSkill __instance, ref uint __result)
    {
        //Add on the alternative levels to the InitLevel?
        //Uses Krafs publicizer to get access to CreatureSkill.creature
        __result += (uint)__instance.creature.GetSkillLevel(__instance.Skill);
    }

    //[HarmonyPostfix]
    //[HarmonyPatch(typeof(CreatureSkill), nameof(CreatureSkill.Base), MethodType.Getter)]
    //public static void PostGetBase(ref CreatureSkill __instance, ref uint __result)
    //{
    //    //Add to Base / Current / whatevs instead of Init
    //    //Not sure what makes most sense
    //    //__result += (uint)__instance.creature.GetSkillLevel(__instance.Skill);
    //}


    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.HandleActionRaiseAttribute), new Type[] { typeof(PropertyAttribute), typeof(uint) })]
    public static bool PreHandleActionRaiseAttribute(PropertyAttribute attribute, uint amount, ref Player __instance, ref bool __result)
    {
        //Pretend amount is the number of levels
        amount = amount > 300 ? 10 : 1u;

        for(var i = 0; i < amount; i++)
            if (!__instance.TryRaiseAttribute(attribute))
                break;

        if (__instance.Attributes.TryGetValue(attribute, out var creatureAttribute))
            __instance.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(__instance, creatureAttribute));

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CreatureVital), nameof(CreatureVital.StartingValue), MethodType.Getter)]
    public static void PostGetStartingValue(ref CreatureVital __instance, ref uint __result)
    {
        __result += (uint)__instance.creature.GetVitalLevel(__instance.Vital);
    }


    [HarmonyPrefix]
    [HarmonyPatch(typeof(Player), nameof(Player.HandleActionRaiseVital), new Type[] { typeof(PropertyAttribute2nd), typeof(uint) })]
    public static bool PreHandleActionRaiseVital(PropertyAttribute2nd vital, uint amount, ref Player __instance, ref bool __result)
    {
        //Pretend amount is the number of levels
        amount = amount > 300 ? 10 : 1u;

        for (var i = 0; i < amount; i++)
            if (!__instance.TryRaiseVital(vital))
                break;

        if (__instance.Vitals.TryGetValue(vital, out var creatureVital))
            __instance.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(__instance, creatureVital));

        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(CreatureAttribute), nameof(CreatureAttribute.StartingValue), MethodType.Getter)]
    public static void PostGetStartingValue(ref CreatureAttribute __instance, ref uint __result)
    {
        __result += (uint)__instance.creature.GetAttributeLevel(__instance.Attribute);
    }


    const int spacing = -20;
    [CommandHandler("levels", AccessLevel.Player, CommandHandlerFlag.RequiresWorld, 0)]
    public static void HandleLevels(Session session, params string[] parameters)
    {
        var player = session.Player;

        var sb = new StringBuilder($"\n{"Level",spacing}{"Cost",spacing}{"Name",spacing}");

        sb.Append($"\n\n============Attributes============");
        foreach (var attr in Enum.GetValues<PropertyAttribute>().OrderBy(x => x.ToString()))
        {
            //Skip skills you can't level?
            if (!player.TryGetAttributeCost(attr, out var cost))
                continue;
            var level = player.GetAttributeLevel(attr);
            sb.Append($"\n{level,spacing}{cost,spacing}{attr,spacing}");
        }

        sb.Append($"\n\n============Vitals============");
        foreach (var attr in Enum.GetValues<PropertyAttribute2nd>().OrderBy(x => x.ToString()))
        {
            //Skip skills you can't level?
            if (!attr.ToString().StartsWith("Max") || !player.TryGetVitalCost(attr, out var cost))
                continue;
            var level = player.GetVitalLevel(attr);
            sb.Append($"\n{level,spacing}{cost,spacing}{attr,spacing}");
        }

        sb.Append($"\n\n============Skills============");
        foreach (var skill in Enum.GetValues<Skill>().OrderBy(x => x.ToString()))
        {
            //Skip skills you can't level?
            if (!player.TryGetSkillCost(skill, out var cost))
                continue;
            var level = player.GetSkillLevel(skill);
            sb.Append($"\n{level,spacing}{cost,spacing}{skill,spacing}");
        }

        player.SendMessage(sb.ToString());
    }
 }

