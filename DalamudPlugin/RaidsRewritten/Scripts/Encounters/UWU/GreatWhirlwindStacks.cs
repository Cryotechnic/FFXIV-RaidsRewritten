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
/// On every Great Whirlwind cast, two to four enumeration towers spawn inside the whirlwind zone.
/// Each party member is assigned a tower by party list index; standing in the wrong tower (or none) fails.
/// </summary>
public class GreatWhirlwindStacks : Mechanic
{
    private const float TowerRadius = 3f;
    private const float TowerVfxNativeRadius = 5f;
    private const float WhirlwindRadius = 12f;
    private const float DuplicatePositionTolerance = 0.1f;
    private const long DuplicatePacketWindowMs = 1000;
    private const float MinimumTowerPuddleGap = 7.5f;
    private const int PartySize = 8;
    private const float OmenDuration = 3.5f;
    private const float HysteriaDuration = 10f;
    private const float MarchDuration = 15f;
    private const int FireResDownId = 0xFE01;
    private const int FireResDownDuration = 3;

    private static readonly string[] TowerOmenVfxByCount =
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
    private readonly List<(Vector3 Position, long SeenAt)> recentWhirlwinds = [];
#if DEBUG
    private readonly List<Entity> debugSpawnedEntities = [];
#endif

    public override void Reset()
    {
        random = new Random(RngSeed);
        recentWhirlwinds.Clear();
        ClearOmens();
        foreach (var attack in attacks)
        {
            if (attack.IsValid()) attack.Destruct();
        }
        attacks.Clear();
#if DEBUG
        UwuDebugSimulate.Cleanup(debugSpawnedEntities);
#endif
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
        if (IsDuplicatePacket(set.Position)) { return; }

        SpawnEnumerationTowers(set.Position);
    }

    private void SpawnEnumerationTowers(Vector3 zoneCenter)
    {
        var requiredPlayers = CreateTowerPlayerCounts();
        var towerPositions = CreateTowerPositions(zoneCenter, requiredPlayers.Length);

        ClearOmens();

        var whirlwindCircle = World.Entity()
            .Set(new StaticVfx("vfx/omen/eff/tatumaki0m.avfx"))
            .Set(new Position(zoneCenter))
            .Set(new Rotation(0f))
            .Set(new Scale(new Vector3(WhirlwindRadius)))
            .Add<Omen>();
        activeOmens.Add(whirlwindCircle);

        for (int i = 0; i < requiredPlayers.Length; i++)
        {
            var towerOmen = World.Entity()
                .Set(new StaticVfx(TowerOmenVfxByCount[requiredPlayers[i] - 1]))
                .Set(new Position(towerPositions[i]))
                .Set(new Rotation(0f))
                .Set(new Scale(new Vector3(TowerRadius / TowerVfxNativeRadius)))
                .Add<Omen>();
            activeOmens.Add(towerOmen);
        }

        var assignedTower = GetAssignedTowerIndex(requiredPlayers);
        var da = DelayedAction.Create(World, () =>
        {
            SnapshotEnumeration(towerPositions, assignedTower);
        }, OmenDuration);
        attacks.Add(da);
    }

    private int[] CreateTowerPlayerCounts()
    {
        int towerCount = random.Next(2, 5);
        var requiredPlayers = new int[towerCount];
        Array.Fill(requiredPlayers, 1);
        int remainingPlayers = PartySize - towerCount;

        while (remainingPlayers > 0)
        {
            int tower = random.Next(requiredPlayers.Length);
            if (requiredPlayers[tower] >= 4) continue;

            requiredPlayers[tower]++;
            remainingPlayers--;
        }

        return requiredPlayers;
    }

    private bool IsDuplicatePacket(Vector3 position)
    {
        var now = Environment.TickCount64;
        recentWhirlwinds.RemoveAll(entry => now - entry.SeenAt > DuplicatePacketWindowMs);

        foreach (var entry in recentWhirlwinds)
        {
            if (Vector2.Distance(entry.Position.ToVector2(), position.ToVector2()) <= DuplicatePositionTolerance)
                return true;
        }

        recentWhirlwinds.Add((position, now));
        return false;
    }

    private Vector3[] CreateTowerPositions(Vector3 zoneCenter, int towerCount)
    {
        var towerPositions = new Vector3[towerCount];
        var angleOffset = RandomTowerOffset ? random.NextSingle() * MathF.PI * 2f : MathF.PI / 4f;
        PlaceEvenlySpacedTowers(towerPositions, zoneCenter, angleOffset);
        return towerPositions;
    }

    private static void PlaceEvenlySpacedTowers(Vector3[] towerPositions, Vector3 zoneCenter, float angleOffset)
    {
        var minimumCenterSeparation = TowerRadius * 2f + MinimumTowerPuddleGap;
        var angleStep = MathF.PI * 2f / towerPositions.Length;
        var dist = MathF.Min(
            MathF.Max(MinimumTowerPuddleGap, minimumCenterSeparation / (2f * MathF.Sin(MathF.PI / towerPositions.Length))),
            WhirlwindRadius - TowerRadius);
        for (int i = 0; i < towerPositions.Length; i++)
        {
            var angle = i * angleStep + angleOffset;
            towerPositions[i] = new Vector3(
                zoneCenter.X + MathF.Sin(angle) * dist,
                zoneCenter.Y,
                zoneCenter.Z + MathF.Cos(angle) * dist);
        }
    }

    private int GetAssignedTowerIndex(int[] requiredPlayers)
    {
        var localPlayer = Dalamud.ObjectTable.LocalPlayer;
        if (localPlayer == null) { return 0; }

        int index = 0;
        foreach (var member in Dalamud.ObjectTable.PlayerObjects)
        {
            if (member.GameObjectId == localPlayer.GameObjectId)
            {
                int assignedIndex = index % PartySize;
                int playerCount = 0;
                for (int tower = 0; tower < requiredPlayers.Length; tower++)
                {
                    playerCount += requiredPlayers[tower];
                    if (assignedIndex < playerCount) return tower;
                }
            }
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
            if (!omen.IsValid()) continue;

            omen.Set(new Alpha(0f));
            omen.Destruct();
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
        if (!UwuDebugSimulate.TryGetPlayerAnchor(Dalamud, out var anchor, out _)) { return; }

        UwuDebugSimulate.Cleanup(debugSpawnedEntities);
        UwuDebugSimulate.SpawnGaruda(EntityManager, anchor, debugSpawnedEntities);
        SpawnEnumerationTowers(anchor);
    }
#endif
}
