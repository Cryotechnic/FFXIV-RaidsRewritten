using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Hooks;
using ECommons.Hooks.ActionEffectTypes;
using Flecs.NET.Core;
using RaidsRewritten.Game;
using RaidsRewritten.Scripts.Conditions;
using Action = Lumina.Excel.Sheets.Action;

namespace RaidsRewritten.Scripts.Encounters.UWU;

public class GarudaMechanicSkipPunishment : Mechanic
{
    private static readonly Vector2 ArenaCenter = new(100f, 100f);
    private const float MarchDuration = 15f;

    private bool sawDownburst;
    private bool sawAwokenWickedWheel;
    private ulong? garudaGameObjectId;
    private bool resolved;

    public override void Reset()
    {
        sawDownburst = false;
        sawAwokenWickedWheel = false;
        garudaGameObjectId = null;
        resolved = false;
    }

    public override void OnDirectorUpdate(DirectorUpdateCategory a3)
    {
        if (a3 == DirectorUpdateCategory.Wipe || a3 == DirectorUpdateCategory.Recommence)
            Reset();
    }

    public override void OnCombatEnd() => Reset();

    public override void OnActionEffectEvent(ActionEffectSet set)
    {
        if (set.Action == null || resolved) return;

        var rowId = set.Action.Value.RowId;
        if (IsGarudaAction(rowId))
            CaptureGaruda(set.Source);
        else if (garudaGameObjectId.HasValue && IsIfritAction(rowId))
        {
            Logger.Info("[UWU] Garuda skip punishment: Ifrit phase action detected.");
            ResolveGarudaDeath();
            return;
        }

        if (!sawDownburst && rowId == UwuData.Garuda.Downburst)
        {
            sawDownburst = true;
            Logger.Info("[UWU] Garuda skip punishment: Downburst observed.");
        }
        else if (!sawAwokenWickedWheel && rowId == UwuData.Garuda.WickedTornado)
        {
            sawAwokenWickedWheel = true;
            Logger.Info("[UWU] Garuda skip punishment: Awoken Wicked Wheel observed.");
        }
    }

    public override void OnStartingCast(Action action, IBattleChara source)
    {
        if (!resolved && IsGarudaAction(action.RowId))
            CaptureGaruda(source);
    }

    public override void OnFrameworkUpdate(IFramework framework)
    {
        if (resolved || !garudaGameObjectId.HasValue) return;

        if (Dalamud.ObjectTable.SearchById(garudaGameObjectId.Value) is IBattleChara { IsDead: true })
        {
            Logger.Info("[UWU] Garuda skip punishment: Garuda death detected.");
            ResolveGarudaDeath();
        }
    }

    private void CaptureGaruda(IGameObject? source)
    {
        if (garudaGameObjectId.HasValue || source is not IBattleChara) return;

        garudaGameObjectId = source.GameObjectId;
        Logger.Info("[UWU] Garuda skip punishment: Garuda captured (0x{0:X}).", source.GameObjectId);
    }

    private static bool IsGarudaAction(uint rowId) => rowId is
        UwuData.Garuda.Slipstream or
        UwuData.Garuda.MistralShriek or
        UwuData.Garuda.GreatWhirlwind or
        UwuData.Garuda.Downburst or
        UwuData.Garuda.AerialBlast or
        UwuData.Garuda.EyeOfTheStorm or
        UwuData.Garuda.WickedWheel or
        UwuData.Garuda.WickedTornado;

    private static bool IsIfritAction(uint rowId) => rowId is
        UwuData.Ifrit.CrimsonCyclone or
        UwuData.Ifrit.RadiantPlume or
        UwuData.Ifrit.Hellfire or
        UwuData.Ifrit.VulcanBurst or
        UwuData.Ifrit.Incinerate or
        UwuData.Ifrit.InfernalFetters or
        UwuData.Ifrit.InfernoHowl or
        UwuData.Ifrit.Eruption or
        UwuData.Ifrit.FlamingCrush;

    private void ResolveGarudaDeath()
    {
        resolved = true;
        Logger.Info(
            "[UWU] Garuda skip punishment check: Downburst={0}, AwokenWickedWheel={1}.",
            sawDownburst,
            sawAwokenWickedWheel);

        if (sawDownburst && sawAwokenWickedWheel)
        {
            Logger.Info("[UWU] Garuda skip punishment: requirements satisfied; no punishment applied.");
            return;
        }

        var player = Dalamud.ObjectTable.LocalPlayer;
        if (player == null || player.IsDead)
        {
            Logger.Info("[UWU] Garuda skip punishment: skipped because the local player is unavailable or dead.");
            return;
        }

        var outward = new Vector3(player.Position.X - ArenaCenter.X, 0f, player.Position.Z - ArenaCenter.Y);
        if (outward.LengthSquared() < 0.001f)
            outward = new Vector3(MathF.Sin(player.Rotation), 0f, MathF.Cos(player.Rotation));

        CommonQueries.LocalPlayerQuery.Each((Entity e, ref Player.Component _) =>
        {
            Logger.Info("[UWU] Garuda skip punishment: applying outward Forced March.");
            ForcedMarch.ApplyMarchToTarget(e, outward, MarchDuration);
        });
    }
}
