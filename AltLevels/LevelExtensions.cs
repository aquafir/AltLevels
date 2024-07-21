using ACE.Database.Models.Auth;
using ACE.Server.WorldObjects.Entity;

namespace AltLevels;
public static class LevelExtensions
{
    public static PropertyInt FakeSkillLevels(this Skill skill) => (PropertyInt)(10000 + skill);
    public static int GetSkillLevel(this Creature player, Skill skill) =>
        player.GetProperty(skill.FakeSkillLevels()) ?? 0;
    public static void SetSkillLevel(this Creature player, Skill skill, int level) =>
        player.SetProperty(skill.FakeSkillLevels(), level);

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
    /// Formula for the cost of the next level of a given SAC for a skill
    /// </summary>
    static double SkillCost(SkillAdvancementClass sac, int level) => sac switch
    {
        SkillAdvancementClass.Untrained => 100 * Math.Pow(level, 1.3),
        SkillAdvancementClass.Trained => 60 * Math.Pow(level, 1.2),
        SkillAdvancementClass.Specialized => 20 * Math.Pow(level, 1.1),
    };

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

    //Getting Total cost or cost to level by X more convenient for refunds / purchasing multiple levels
    //public static uint GetTotalCost(int stepNumber, int qty) => GetTotalCost(stepNumber + qty) - GetTotalCost(stepNumber);
    //public static uint GetTotalCost(int stepNumber) => (uint)(multiplier == 1d ? (basePrice * stepNumber) : (basePrice * ((1 - Math.Pow(multiplier, stepNumber)) / (1 - multiplier))));

    //public static int Purchaseable(int currency, int owned) => (int)Math.Floor(Math.Log(Math.Pow(multiplier, (double)owned) - (currency * (1 - multiplier) / basePrice), multiplier) - owned);
    //public static int CurrentLevel(int totalSpent) => Purchaseable(totalSpent, 0);
}
