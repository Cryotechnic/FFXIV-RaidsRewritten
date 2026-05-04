using System;
using System.Numerics;
using ECommons.MathHelpers;
using Flecs.NET.Core;
using RaidsRewritten.Game;
using RaidsRewritten.Log;
using RaidsRewritten.Scripts.Attacks.Omens;
using RaidsRewritten.Scripts.Components;
using RaidsRewritten.Utility;

namespace RaidsRewritten.Scripts.Attacks;

/// <summary>
/// A timed tower that shows a circle omen for a set duration, then resolves.
/// If the local player is not inside the tower radius at resolution time,
/// <see cref="Component.OnFail"/> is invoked.
/// </summary>
public class EnumerationTower(DalamudServices dalamud, CommonQueries commonQueries, ILogger logger) : IEntity, ISystem
{
    public enum Phase { Omen, Resolved }

    public record struct Component(
        float Duration,
        float Radius,
        Action<Entity>? OnFail,
        float TimeElapsed = 0,
        Phase Phase = Phase.Omen,
        Entity OmenEntity = default);

    public Entity Create(World world)
    {
        return world.Entity()
            .Set(new Position())
            .Set(new Rotation())
            .Set(new Component())
            .Add<Attack>();
    }

    public void Register(World world)
    {
        world.System<Component, Position>()
            .Each((Iter it, int i, ref Component component, ref Position position) =>
            {
                var entity = it.Entity(i);
                component.TimeElapsed += it.DeltaTime();

                switch (component.Phase)
                {
                    case Phase.Omen:
                        if (!component.OmenEntity.IsValid())
                        {
                            component.OmenEntity = CircleOmen.CreateEntity(it.World())
                                .Set(new Scale(new Vector3(component.Radius, component.Radius, component.Radius)));
                            component.OmenEntity
                                .Set(new Position(position.Value))
                                .Set(new Rotation())
                                .Set(new OmenDuration(component.Duration, false))
                                .ChildOf(entity);
                        }

                        if (component.TimeElapsed >= component.Duration)
                        {
                            if (component.OmenEntity.IsValid())
                                component.OmenEntity.Destruct();

                            ResolveForLocalPlayer(component, position);
                            component.Phase = Phase.Resolved;
                        }
                        break;

                    case Phase.Resolved:
                        entity.Destruct();
                        break;
                }
            });
    }

    private void ResolveForLocalPlayer(Component component, Position position)
    {
        try
        {
            var player = dalamud.ObjectTable.LocalPlayer;
            if (player == null || player.IsDead || component.OnFail == null)
                return;

            var dist = Vector2.Distance(position.Value.ToVector2(), player.Position.ToVector2());
            if (dist > component.Radius)
            {
                commonQueries.LocalPlayerQuery.Each((Entity e, ref Player.Component _) =>
                {
                    component.OnFail(e);
                });
            }
        }
        catch (Exception e)
        {
            logger.Error(e.ToStringFull());
        }
    }
}
