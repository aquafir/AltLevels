using ACE.Database.Models.Auth;
using ACE.Server.WorldObjects.Entity;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace AltLevels;
public static class LevelExtensions
{
    const int PROPERTY_START = 20800;
    #region Properties
    public static PropertyInt FakeSkillLevels(this Skill skill) => (PropertyInt)(PROPERTY_START + skill);
    public static PropertyInt AltAttributeLevels(this PropertyAttribute attribute) => (PropertyInt)(PROPERTY_START + 55 + attribute);
    public static PropertyInt AltVitalLevels(this PropertyAttribute2nd vital) => (PropertyInt)(PROPERTY_START + 62 + vital);

    public static int GetSkillLevel(this Creature player, Skill skill) =>
        player.GetProperty(skill.FakeSkillLevels()) ?? 0;
    public static void SetSkillLevel(this Creature player, Skill skill, int level) =>
        player.SetProperty(skill.FakeSkillLevels(), level);

    public static int GetAttributeLevel(this Creature player, PropertyAttribute attribute) =>
        player.GetProperty(attribute.AltAttributeLevels()) ?? 0;
    public static void SetAttributeLevel(this Creature player, PropertyAttribute attribute, int level) =>
        player.SetProperty(attribute.AltAttributeLevels(), level);

    public static int GetVitalLevel(this Creature player, PropertyAttribute2nd vital) =>
        player.GetProperty(vital.AltVitalLevels()) ?? 0;
    public static void SetVitalLevel(this Creature player, PropertyAttribute2nd vital, int level) =>
        player.SetProperty(vital.AltVitalLevels(), level);
    #endregion

    #region Cost Functions
    static double SkillCost(SkillAdvancementClass sac, int level) => sac switch
    {
        SkillAdvancementClass.Untrained => 100 * Math.Pow(level, 1.3),
        SkillAdvancementClass.Trained => 60 * Math.Pow(level, 1.2),
        SkillAdvancementClass.Specialized => 20 * Math.Pow(level, 1.1),
    };

    static double AttributeCost(int level) => 100 * Math.Pow(level, 1.3);
    static double VitalCost(int level) => 100 * Math.Pow(level, 1.3);
    #endregion

    #region Skills
    /// <summary>
    /// Tries to get the cost needed to raise a skill by one level
    /// </summary>
    public static bool TryGetSkillCost(this Creature player, Skill skill, out long cost)
    {
        cost = long.MaxValue;

        //Verify player can spend xp on skill
        var creatureSkill = player.GetCreatureSkill(skill, false);
        if (creatureSkill == null || creatureSkill.AdvancementClass < SkillAdvancementClass.Trained)
            return false;

        //Use fake skill level and trained status to get a cost for the next level
        var currentLevel = player.GetSkillLevel(skill);
        cost = Convert.ToInt64(SkillCost(creatureSkill.AdvancementClass, currentLevel));

        return true;
    }

    /// <summary>
    /// Handles an attempt from client to raise a skill by one
    /// </summary>
    public static bool TryRaiseSkill(this Player player, Skill skill)
    {
        //Assume the amount the client supplies is always looking to raise the level once
        if (!player.TryGetSkillCost(skill, out var cost))
        {
            player.SendMessage($"Failed to get skill cost for {skill}");
            return false;
        }

        if (cost > player.AvailableExperience)
        {
            player.SendMessage($"Insufficient XP to raise {skill}, {cost-player.AvailableExperience} needed.");
            return false;
        }

        //Try to spend xp
        if (!player.SpendXP(cost))
        {
            player.SendMessage($"Failed to spend {cost} to raise {skill}");
            return false;
        }

        //All clear to add the alternative level
        var creatureSkill = player.GetCreatureSkill(skill, false);
        var level = player.GetSkillLevel(skill) + 1;
        player.SetSkillLevel(skill, level);

        //Update skill
        player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateSkill(player, creatureSkill));
        player.SendMessage($"Your base {skill.ToSentence()} skill is now {level}, costing {cost}!", ChatMessageType.Advancement);

        if (skill == Skill.Run && PropertyManager.GetBool("runrate_add_hooks").Item)
            player.HandleRunRateUpdate();


        return true;
    }
    #endregion

    #region Attribute
    /// <summary>
    /// Tries to get the cost needed to raise a skill by one level
    /// </summary>
    public static bool TryGetAttributeCost(this Creature player, PropertyAttribute attribute, out long cost)
    {
        cost = long.MaxValue;

        //Verify player can spend xp on Attribute
        if(!player.Attributes.TryGetValue(attribute, out var creatureAttribute))
            return false;

        //Use fake Attribute level and trained status to get a cost for the next level
        var currentLevel = player.GetAttributeLevel(attribute);
        cost = Convert.ToInt64(AttributeCost(currentLevel));

        return true;
    }

    /// <summary>
    /// Handles an attempt from client to raise a Attribute by one
    /// </summary>
    public static bool TryRaiseAttribute(this Player player, PropertyAttribute attribute)
    {
        //Assume the amount the client supplies is always looking to raise the level once
        if (!player.TryGetAttributeCost(attribute, out var cost))
        {
            player.SendMessage($"Failed to get Attribute cost for {attribute}");
            return false;
        }

        if (cost > player.AvailableExperience)
        {
            player.SendMessage($"Insufficient XP to raise {attribute}, {cost - player.AvailableExperience} needed.");
            return false;
        }

        //Try to spend xp
        if (!player.SpendXP(cost))
        {
            player.SendMessage($"Failed to spend {cost} to raise {attribute}");
            return false;
        }

        if (!player.Attributes.TryGetValue(attribute, out var creatureAttribute))
        {
            player.SendMessage($"Failed to find attribute {attribute}");
            return false;
        }

        //All clear to add the alternative level
        var level = player.GetAttributeLevel(attribute) + 1;
        player.SetAttributeLevel(attribute, level);

        //Update Attribute
        //player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateAttribute(player, creatureAttribute));
        player.SendMessage($"Your base {attribute} is now {level}, costing {cost}!", ChatMessageType.Advancement);

        return true;
    }
    #endregion

    #region Vital
    /// <summary>
    /// Tries to get the cost needed to raise a skill by one level
    /// </summary>
    public static bool TryGetVitalCost(this Creature player, PropertyAttribute2nd vital, out long cost)
    {
        cost = long.MaxValue;

        //Verify player can spend xp on vital
        var creatureVital = player.GetCreatureVital(vital);

        //Use fake vital level and trained status to get a cost for the next level
        var currentLevel = player.GetVitalLevel(vital);
        cost = Convert.ToInt64(VitalCost(currentLevel));

        return true;
    }

    /// <summary>
    /// Handles an attempt from client to raise a vital by one
    /// </summary>
    public static bool TryRaiseVital(this Player player, PropertyAttribute2nd vital)
    {
        //Assume the amount the client supplies is always looking to raise the level once
        if (!player.TryGetVitalCost(vital, out var cost))
        {
            player.SendMessage($"Failed to get vital cost for {vital}");
            return false;
        }

        if (cost > player.AvailableExperience)
        {
            player.SendMessage($"Insufficient XP to raise {vital}, {cost - player.AvailableExperience} needed.");
            return false;
        }

        //Try to spend xp
        if (!player.SpendXP(cost))
        {
            player.SendMessage($"Failed to spend {cost} to raise {vital}");
            return false;
        }

        if (!player.Vitals.TryGetValue(vital, out var creatureVital))
        {
            player.SendMessage($"Failed to find vital {vital}");
            return false;
        }

        //All clear to add the alternative level
        var level = player.GetVitalLevel(vital) + 1;
        player.SetVitalLevel(vital, level);

        //Update vital
        //player.Session.Network.EnqueueSend(new GameMessagePrivateUpdateVital(player, creatureVital));
        player.SendMessage($"Your base {vital.ToSentence()} is now {level}, costing {cost}!", ChatMessageType.Advancement);

        return true;
    }
    #endregion

    //Getting Total cost or cost to level by X more convenient for refunds / purchasing multiple levels
    //public static uint GetTotalCost(int stepNumber, int qty) => GetTotalCost(stepNumber + qty) - GetTotalCost(stepNumber);
    //public static uint GetTotalCost(int stepNumber) => (uint)(multiplier == 1d ? (basePrice * stepNumber) : (basePrice * ((1 - Math.Pow(multiplier, stepNumber)) / (1 - multiplier))));

    //public static int Purchaseable(int currency, int owned) => (int)Math.Floor(Math.Log(Math.Pow(multiplier, (double)owned) - (currency * (1 - multiplier) / basePrice), multiplier) - owned);
    //public static int CurrentLevel(int totalSpent) => Purchaseable(totalSpent, 0);
}
