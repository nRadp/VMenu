using Il2CppInterop.Runtime;
using ProjectM;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace ExtrasensoryPerception.API;

public static class VExtensions
{
    private static EntityManager EntityManager => VWorld.EntityManager;
    private static PrefabLookupMap PrefabLookupMap => VWorld.PrefabLookupMap;

    public static void Write<T>(this Entity entity, T componentData) where T : struct
    {
        EntityManager.SetComponentData(entity, componentData);
    }

    public static T Read<T>(this Entity entity) where T : struct
    {
        return EntityManager.GetComponentData<T>(entity);
    }

    public static DynamicBuffer<T> ReadBuffer<T>(this Entity entity) where T : struct
    {
        return EntityManager.GetBuffer<T>(entity);
    }

    public static bool HasBuffer<T>(this Entity entity) where T : struct
    {
        return EntityManager.HasBuffer<T>(entity);
    }

    public static bool TryGetComponent<T>(this Entity entity, out T componentData) where T : struct
    {
        componentData = default;

        if (entity.Has<T>())
        {
            componentData = entity.Read<T>();
            return true;
        }

        return false;
    }

    public static bool Has<T>(this Entity entity) where T : struct
    {
        return EntityManager.HasComponent(entity, new(Il2CppType.Of<T>()));
    }

    public static NativeArray<ComponentType> GetAllComponents(this Entity entity)
    {
        return EntityManager.GetComponentTypes(entity);
    }

    public static bool Exists(this Entity entity)
    {
        return entity.HasValue() && entity.IndexWithinCapacity() && EntityManager.Exists(entity);
    }

    private static bool HasValue(this Entity entity)
    {
        return entity != Entity.Null;
    }
    
    private static bool IndexWithinCapacity(this Entity entity)
    {
        return  entity.Index < EntityManager.EntityCapacity;
    }

    public static string GetName(this Entity entity)
    {
        return entity.TryGetComponent<PrefabGUID>(out var prefabGuid) ? PrefabLookupMap.GetName(prefabGuid) : "";
    }

    public static PrefabGUID GetPrefabGuid(this Entity entity)
    {
        return entity.TryGetComponent<PrefabGUID>(out var prefabGuid) ? prefabGuid : PrefabGUID.Empty;
    }

    public static Vector3 GetPosition(this Entity entity)
    {
        return entity.Read<LocalToWorld>().Position;
    }

    public static quaternion GetRotation(this Entity entity)
    {
        return entity.TryGetComponent<LocalTransform>(out var localTransform) ? localTransform.Rotation : quaternion.identity;
    }

    public static bool IsAlive(this Entity entity)
    {
        if (entity.TryGetComponent<Health>(out var health)) return health is { IsDead: false, Value: > 0 };
        return false;
    }

    public static bool IsAlly(this Entity entity, Entity otherEntity)
    {
        if (entity.TryGetComponent<Team>(out var team) && otherEntity.TryGetComponent<Team>(out var otherTeam))
            return team.Value == otherTeam.Value;
        return false;
    }

    public static bool IsDisabled(this Entity entity)
    {
        return entity.Has<Disabled>();
    }

    public static bool IsPlayer(this Entity entity)
    {
        return entity.Has<PlayerCharacter>() || entity.Has<VampireTag>();
    }

    public static bool IsVBlood(this Entity entity)
    {
        return entity.Has<VBloodConsumeSource>();
    }

    public static bool IsGateBoss(this Entity entity)
    {
        return entity.Has<VBloodUnit>() && !entity.Has<VBloodConsumeSource>();
    }
}
