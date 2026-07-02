using System.Numerics;
using Flecs.NET.Core;
using RaidsRewritten.Scripts.Components;

namespace RaidsRewritten.Scripts.Conditions;

/// <summary>
/// Prevents all actions without restricting movement.
/// </summary>
public class Silence
{
    private const string IconId = "215017";

    public record struct Component(object _);

    public static void ApplyToTarget(
        Entity target,
        float duration,
        bool extendDuration = false,
        bool overrideExistingDuration = false)
    {
        ApplyToTarget(target, duration, ConditionTable.Id.Silence, extendDuration, overrideExistingDuration);
    }

    public static void ApplyToTarget(
        Entity target,
        float duration,
        BigInteger id,
        bool extendDuration = false,
        bool overrideExistingDuration = false,
        bool isClientControlled = true)
    {
        DelayedAction.Create(target.CsWorld(), (ref Iter it) =>
        {
            var condition = Condition.ApplyToTarget(target, "Silenced", duration, id, extendDuration, overrideExistingDuration, isClientControlled);

            condition
                .Set(new Condition.StatusIconReplacement(IconId, ConditionTable.IconToReplace.Silence))
                .Set(new Condition.Status(ConditionTable.IconToReplace.Silence, "Silence", "Unable to execute actions."))
                .Set(new Condition.StatusTooltip("Silence (RaidsRewritten)"))
                .Add<Condition.StatusEnfeeblement>();

            if (!condition.Has<Component>())
            {
                condition.Set(new Component());
            }
        }, 0, true).ChildOf(target);
    }
}
