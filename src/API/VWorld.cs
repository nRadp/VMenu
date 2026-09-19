using System;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using UnityEngine;

namespace ExtrasensoryPerception.API;

public static class VWorld
{
    private static PrefabLookupMap? _prefabLookupMap;

    public static PrefabLookupMap PrefabLookupMap
    {
        get
        {
            _prefabLookupMap ??= PrefabCollectionSystem.GetPrefabLookupMap(Game);
            return _prefabLookupMap ?? throw new InvalidOperationException();
        }
    }

    public static EntityManager EntityManager => Game.EntityManager;
    private static World? _world;

    private static World Server
    {
        get
        {
            if (_world is { IsCreated: true }) return _world;

            _world = GetWorld("Server") ?? throw new Exception("There is no Server world (yet).");
            return _world;
        }
        set => _world = value;
    }

    private static World Client
    {
        get
        {
            if (_world is { IsCreated: true }) return _world;

            _world = GetWorld("Client_0") ?? throw new Exception("There is no Client world (yet).");
            return _world;
        }
        set => _world = value;
    }

    public static World Default => World.DefaultGameObjectInjectionWorld;

    public static World Game
    {
        get => IsClient ? Client : Server;
        set => _world = value;
    }

    private static bool IsClient => Application.productName == "VRising";

    public static void Reset()
    {
        _world = null;
        _prefabLookupMap = null;
    }

    private static World? GetWorld(string name)
    {
        foreach (var world in World.s_AllWorlds)
            if (world.Name == name)
                return world;

        return null;
    }
}