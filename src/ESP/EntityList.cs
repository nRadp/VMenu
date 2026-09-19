using System;
using ExtrasensoryPerception.API;
using ProjectM;
using ProjectM.Network;
using ProjectM.Presentation;
using ProjectM.Shared;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;

namespace ExtrasensoryPerception.ESP;

internal static class EntityList
{
    private static EntityManager EntityManager => VWorld.EntityManager;

    private static Entity _localCharacter = Entity.Null;
    private static Entity _localUser = Entity.Null;

    public static Entity LocalCharacter
    {
        get
        {
            if (_localCharacter != Entity.Null && _localCharacter.Exists()) return _localCharacter;
            _localCharacter = ConsoleShared.TryGetLocalCharacterInCurrentWorld(out var localCharacter, VWorld.Game)
                ? localCharacter
                : Entity.Null;
            return _localCharacter;
        }
    }

    public static Entity LocalUser
    {
        get
        {
            if (_localUser != Entity.Null && _localUser.Exists()) return _localUser;
            _localUser = ConsoleShared.TryGetLocalUserInCurrentWorld(out var localUser, VWorld.Game)
                ? localUser
                : Entity.Null;
            return _localUser;
        }
    }

    private static EntityQuery _localPlayerQuery;
    internal static EntityQuery Players;
    internal static EntityQuery VBloodCarriers;
    internal static EntityQuery Mobs;
    internal static EntityQuery GateBosses;
    internal static EntityQuery Containers;
    internal static EntityQuery Items;
    internal static EntityQuery Ores;
    internal static EntityQuery Plants;
    internal static EntityQuery FishingSpots;
    internal static EntityQuery Servants;
    internal static EntityQuery Horses;
    internal static EntityQuery Carriages;
    internal static EntityQuery CastleHearts;

    internal static void ResetState()
    {
        _localCharacter = Entity.Null;
        _localUser = Entity.Null;
        _localPlayerQuery = default;
        Players = default;
        VBloodCarriers = default;
        Mobs = default;
        GateBosses = default;
        Containers = default;
        Items = default;
        Ores = default;
        Plants = default;
        FishingSpots = default;
        Servants = default;
        Horses = default;
        Carriages = default;
        CastleHearts = default;
    }

    internal static void InitializeQueries()
    {
        ResetState();
        _localPlayerQuery = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<User>());

        Players = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerCharacter>());

        VBloodCarriers = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<VBloodConsumeSource>() },
            None = new[] { ComponentType.ReadOnly<PlayerCharacter>() }
        });

        Mobs = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<BloodConsumeSource>() },
            None = new[]
            {
                ComponentType.ReadOnly<VBloodConsumeSource>(),
                ComponentType.ReadOnly<VBloodUnit>()
            }
        });

        GateBosses = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<VBloodUnit>() },
            None = new[] { ComponentType.ReadOnly<VBloodConsumeSource>() }
        });

        Containers = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Interactable>(),
                ComponentType.ReadOnly<InventoryOwner>()
            },
            Any = new[]
            {
                ComponentType.ReadOnly<PlacementDestroyData>(),
                ComponentType.ReadOnly<PlayerDeathContainer>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<BlueprintData>(),
                ComponentType.ReadOnly<DismantleDestroyData>(),
                ComponentType.ReadOnly<ServantEquipment>(),
                ComponentType.ReadOnly<TakeDamageInSun>(),
                ComponentType.ReadOnly<Trader>()
            }
        });

        Items = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Interactable>(),
                ComponentType.ReadOnly<InteractedUpon>(),
                ComponentType.ReadOnly<ItemPickup>()
            },
            None = new[] { ComponentType.ReadOnly<PlayerDeathContainer>() }
        });

        Ores = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<SpawnPhysicsObjectOnDeath>(),
                ComponentType.ReadOnly<MoveStopTrigger>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<CanFadeOut>()
            }
        });

        Plants = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<DestroyData>(),
                ComponentType.ReadOnly<DestroyState>(),
                ComponentType.ReadOnly<PlacementDestroyData>(),
                ComponentType.ReadOnly<TilePlacementTag>()
            },
            None = new[]
            {
                ComponentType.ReadOnly<ChildPrefabTag>(),
                ComponentType.ReadOnly<CritterSpawn>(),
                ComponentType.ReadOnly<Parent>(),
                ComponentType.ReadOnly<PhysicsCollider>(),
                ComponentType.ReadOnly<PhysicsWorldIndex>(),
                ComponentType.ReadOnly<PostTransformMatrix>(),
                ComponentType.ReadOnly<SpawnPhysicsObjectOnDeath>(),
                ComponentType.ReadOnly<StaticChild>()
            }
        });

        FishingSpots = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<Age>(),
                ComponentType.ReadOnly<Buffable>(),
                ComponentType.ReadOnly<DestroyAfterDuration>(),
                ComponentType.ReadOnly<Torture>()
            }
        });

        Servants = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<ServantEquipment>() },
            None = new[]
            {
                ComponentType.ReadOnly<BlueprintData>(),
                ComponentType.ReadOnly<DismantleDestroyData>(),
                ComponentType.ReadOnly<TakeDamageInSun>()
            }
        });

        Horses = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Mountable>());
        Carriages = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Script_CarriageData>());

        CastleHearts = EntityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[]
            {
                ComponentType.ReadOnly<ProjectM.CastleBuilding.CastleHeart>(),
                ComponentType.ReadOnly<Interactable>()
            }
        });
    }

    internal static Entity GetLocalPlayer()
    {
        return !_localPlayerQuery.IsEmpty ? _localPlayerQuery.GetSingletonEntity() : Entity.Null;
    }

    internal static void ForEach(this EntityQuery query, Action<Entity> action)
    {
        if (query.IsEmpty) return;

        var entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            for (var i = 0; i < entities.Length; i++)
            {
                action(entities[i]);
            }
        }
        finally
        {
            if (entities.IsCreated) entities.Dispose();
        }
    }
}
