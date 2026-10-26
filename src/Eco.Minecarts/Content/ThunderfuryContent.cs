namespace Eco.Mods.TechTree;

using System.ComponentModel;
using Eco.Gameplay.DynamicValues;
using Eco.Shared.Localization;
using Eco.Shared.Serialization;

// An administrator-spawned Easter egg. Keep the native modern machete's
// harvesting, swing, calories, repairs and durability; change animal damage only.
[Serialized, Category("Hidden")]
[LocDisplayName("Thunderfury, Blessed Blade of the Windseeker")]
[LocDescription("Chance on hit: Blasts your enemy with lightning, dealing 25 Nature damage and then jumping to additional nearby enemies. The blast reaches five targets and weakens Nature resistance; a cyclone slows the first target's attacks by 20% for 12 seconds.")]
public sealed class ThunderfuryItem : ModernMacheteItem
{
    public const float AnimalDamageMultiplier = 1.10f;
    private readonly IDynamicValue animalDamage;

    public ThunderfuryItem() => animalDamage = new MultiDynamicValue(
        MultiDynamicOps.Multiply, base.AnimalDamage, new ConstantValue(AnimalDamageMultiplier));

    public override IDynamicValue AnimalDamage => animalDamage;
}
