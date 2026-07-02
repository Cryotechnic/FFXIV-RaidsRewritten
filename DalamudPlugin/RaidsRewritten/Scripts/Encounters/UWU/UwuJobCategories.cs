using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.ExcelServices;
using ECommons.GameFunctions;

namespace RaidsRewritten.Scripts.Encounters.UWU;

public static class UwuJobCategories
{
    private static readonly HashSet<Job> TankJobs =
    [
        Job.PLD, Job.WAR, Job.DRK, Job.GNB,
    ];

    private static readonly HashSet<Job> MeleeJobs =
    [
        Job.MNK, Job.DRG, Job.NIN, Job.SAM, Job.RPR, Job.VPR,
    ];

    private static readonly HashSet<Job> CasterJobs =
    [
        Job.BLM, Job.SMN, Job.RDM, Job.PCT,
    ];

    public static bool IsTank(ICharacter? character) =>
        character is IPlayerCharacter pc && TankJobs.Contains((Job)pc.ClassJob.RowId);

    public static bool IsMelee(ICharacter? character) =>
        character is IPlayerCharacter pc && MeleeJobs.Contains((Job)pc.ClassJob.RowId);

    public static bool IsCaster(ICharacter? character) =>
        character is IPlayerCharacter pc && CasterJobs.Contains((Job)pc.ClassJob.RowId);

    public static bool IsHealer(ICharacter? character) =>
        character is IPlayerCharacter pc && pc.GetRole() == CombatRole.Healer;
}
