using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Hooks;
using ECommons.Hooks.ActionEffectTypes;
using ECommons.MathHelpers;
using Flecs.NET.Core;
using RaidsRewritten.Game;
using RaidsRewritten.Scripts.Attacks;
using RaidsRewritten.Scripts.Components;
using RaidsRewritten.Scripts.Conditions;
using RaidsRewritten.Spawn;
using RaidsRewritten.Utility;

namespace RaidsRewritten.Scripts.Encounters.UWU;

/// <summary>
/// On every Great Whirlwind cast, four enumeration towers spawn inside the whirlwind zone.
/// Each party member is assigned a tower by party list index; standing in the wrong tower (or none) fails.
/// </summary>
public class GreatWhirlwindStacks : Mechanic
{
    private const float TowerRadius = 3f;
    private const float WhirlwindRadius = 12f;
    private const float OmenDuration = 3.5f;
    private const float HysteriaDuration = 10f;
    private const float MarchDuration = 15f;
    private const int FireResDownId = 0xFE01;
    private const int FireResDownDuration = 15;

    private static readonly string[] TowerOmenVfxByNumber =
    [
        "vfx/omen/eff/m0119_trap_01t.avfx",
        "vfx/omen/eff/general_trap_o2x.avfx",
        "vfx/omen/eff/general_trap_o3x.avfx",
        "vfx/omen/eff/general_trap_o4x.avfx",
    ];

    public int RngSeed { get; set; }
    public bool RandomTowerOffset { get; set; } = true;
    private Random random = new();

    private readonly List<Entity> attacks = [];
    private readonly List<Entity> activeOmens = [];

    public override void Reset()
    {
        random = new Random(RngSeed);
        ClearOmens();
        foreach (var attack in attacks)
        {
            if (attack.IsValid()) attack.Destruct();
        }
        attacks.Clear();
    }

    public override void OnDirectorUpdate(DirectorUpdateCategory a3)
    {
        if (a3 == DirectorUpdateCategory.Wipe || a3 == DirectorUpdateCategory.Recommence)
            Reset();
    }

    public override void OnCombatEnd() => Reset();

    public override void OnActionEffectEvent(ActionEffectSet set)
    {
        if (set.Action == null) { return; }
        if (set.Action.Value.RowId != UwuData.Garuda.GreatWhirlwind) { return; }

        SpawnEnumerationTowers(set.Position);
    }

    private void SpawnEnumerationTowers(Vector3 zoneCenter)
    {
        var towerPositions = new Vector3[4];
        for (int i = 0; i < 4; i++)
        {
            if (RandomTowerOffset)
            {
                var angle = random.NextSingle() * MathF.PI * 2f;
                var dist = (0.25f + random.NextSingle() * 0.55f) * WhirlwindRadius;
                towerPositions[i] = new Vector3(
                    zoneCenter.X + MathF.Sin(angle) * dist,
                    zoneCenter.Y,
                    zoneCenter.Z + MathF.Cos(angle) * dist);
            }
            else
            {
                var angle = i * MathF.PI / 2f + MathF.PI / 4f;
                var dist = WhirlwindRadius * 0.55f;
                towerPositions[i] = new Vector3(
                    zoneCenter.X + MathF.Sin(angle) * dist,
                    zoneCenter.Y,
                    zoneCenter.Z + MathF.Cos(angle) * dist);
            }
        }

#if DEBUG
        foreach (var omen in activeOmens)
        {
            if (omen.IsValid()) omen.Destruct();
        }
        activeOmens.Clear();

        var whirlwindCircle = World.Entity()
            .Set(new StaticVfx("vfx/omen/eff/tatumaki0m.avfx"))
            .Set(new Position(zoneCenter))
            .Set(new Rotation(0f))
            .Set(new Scale(new Vector3(WhirlwindRadius)))
            .Add<Omen>();
        activeOmens.Add(whirlwindCircle);

        for (int i = 0; i < 4; i++)
        {
            var towerOmen = World.Entity()
                .Set(new StaticVfx(TowerOmenVfxByNumber[i]))
                .Set(new Position(towerPositions[i]))
                .Set(new Rotation(0f))
                .Set(new Scale(new Vector3(3f, 5f, 3f)))
                .Add<Omen>();
            activeOmens.Add(towerOmen);
        }
#endif

        var assignedTower = GetAssignedTowerIndex();
        var da = DelayedAction.Create(World, () =>
        {
            SnapshotEnumeration(towerPositions, assignedTower);
        }, OmenDuration);
        attacks.Add(da);
    }

    private int GetAssignedTowerIndex()
    {
        var localPlayer = Dalamud.ObjectTable.LocalPlayer;
        if (localPlayer == null) { return 0; }

        int index = 0;
        foreach (var member in Dalamud.ObjectTable.PlayerObjects)
        {
            if (member.GameObjectId == localPlayer.GameObjectId)
                return index % 4;
            index++;
        }
        return 0;
    }

    private void SnapshotEnumeration(Vector3[] towerPositions, int assignedTower)
    {
        ClearOmens();

        var player = Dalamud.ObjectTable.LocalPlayer;
        if (player == null || player.IsDead) { return; }

        var playerPos2 = player.Position.ToVector2();
        var assignedPos2 = towerPositions[assignedTower].ToVector2();
        bool inAssignedTower = Vector2.Distance(playerPos2, assignedPos2) <= TowerRadius;

        if (inAssignedTower)
        {
            CommonQueries.LocalPlayerQuery.Each((Entity e, ref Player.Component _) =>
            {
                bool alreadyDebuffed = false;
                using var q = e.CsWorld().QueryBuilder<Condition.Component, Condition.Id>()
                    .With(Ecs.ChildOf, e).Build();
                q.Each((ref Condition.Component _, ref Condition.Id id) =>
                {
                    if (id.Value == FireResDownId) alreadyDebuffed = true;
                });

                if (alreadyDebuffed)
                    Hysteria.ApplyToTarget(e, HysteriaDuration, 0.5f);
                else
                    ApplyFireResistanceDown(e, FireResDownDuration);
            });
            return;
        }

        if (player.HasTranscendance())
        {
            this.VfxSpawn.PlayInvulnerabilityEffect(player);
            return;
        }

        CommonQueries.LocalPlayerQuery.Each((Entity e, ref Player.Component _) =>
        {
            var forward = new Vector3(MathF.Sin(player.Rotation), 0, MathF.Cos(player.Rotation));
            ForcedMarch.ApplyMarchToTarget(e, forward, MarchDuration);
        });
    }

    private void ClearOmens()
    {
        foreach (var omen in activeOmens)
        {
            if (omen.IsValid()) omen.Destruct();
        }
        activeOmens.Clear();
    }

    private static void ApplyFireResistanceDown(Entity target, float duration)
    {
        DelayedAction.Create(target.CsWorld(), (ref Iter it) =>
        {
            var condition = Condition.ApplyToTarget(target, "Fire Resistance Down", duration, FireResDownId, false, false);
            condition
                .Set(new Condition.Status(215595, "Fire Resistance Down", "Fire resistance is reduced."))
                .Set(new Condition.StatusTooltip("Fire Resistance Down (RaidsRewritten)"))
                .Add<Condition.StatusEnfeeblement>();
        }, 0, true).ChildOf(target);
    }

#if DEBUG
    public override void DebugSimulate()
    {
        var player = Dalamud.ObjectTable.LocalPlayer;
        if (player == null) { return; }
        SpawnEnumerationTowers(player.Position);
    }
#endif
}
