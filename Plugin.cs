using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NavalBuffetMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        public class ContainerConfig
        {
            public ConfigEntry<bool> Replenishable;
            public ConfigEntry<float> Radius;
            public ConfigEntry<float> CheckInterval; 
            public ConfigEntry<float> UnitCooldown; // Per-unit replenishment cooldown
        }

        public static Dictionary<string, ContainerConfig> Configs = new Dictionary<string, ContainerConfig>();

        // Virtual Trigger
        public class RearmerData
        {
            public bool IsManaged = false;
            public float LastCheckTime = 0f;
            public float CheckInterval = 1f;
            public float Radius = 1000f;
            public float UnitCooldown = 10f; // Cached from config
            
            // Pre-allocated buffers to prevent Memory Spikes
            public Collider[] HitBuffer = new Collider[500]; 
            public HashSet<Collider> KnownColliders = new HashSet<Collider>();
            public HashSet<Collider> CurrentHits = new HashSet<Collider>();
        }
        public static ConditionalWeakTable<Rearmer, RearmerData> RearmerCache = new ConditionalWeakTable<Rearmer, RearmerData>();
        
        // Tracks when each individual unit was last replenished (globally across all rearmers)
        public static ConditionalWeakTable<Unit, StrongBox<float>> UnitLastReplenishTime = new ConditionalWeakTable<Unit, StrongBox<float>>();

        private void Awake()
        {
            Log = Logger;
            
            string[] targets = { 
                "MunitionsContainer1", "MunitionsPallet1", 
                "NavalSupplyContainer1", "NavalPallet1", 
                "AmmunitionBunker1", "AmmoDump1" 
            };
            
            foreach (var target in targets)
            {
                Configs[target] = new ContainerConfig
                {
                    Replenishable = Config.Bind(target, "Replenishable", true, "Whether the container can be used multiple times."),
                    Radius = Config.Bind(target, "Radius", 1000f, "Resupply radius."),
                    CheckInterval = Config.Bind(target, "CheckInterval", 1.0f, "Throttle (in seconds) to prevent memory crashes."),
                    UnitCooldown = Config.Bind(target, "UnitCooldown", 30f, "Individual unit replenishment cooldown (in seconds) to prevent nonstop firing.")
                };
            }

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            
            Log.LogInfo("NavalBuffetMod initialized. GC-Free Virtual Trigger system engaged.");
        }
    }

    [HarmonyPatch(typeof(Rearmer), "Start")]
    public class Rearmer_Start_Patch
    {
        static void Postfix(Rearmer __instance)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null) return;
                
                string objName = __instance.gameObject.name;
                
                foreach (var kvp in Plugin.Configs)
                {
                    if (objName.Contains(kvp.Key))
                    {
                        var config = kvp.Value;
                        Plugin.Log.LogInfo($"[NavalBuffetMod] Spawning managed Rearmer: '{objName}'.");

                        var data = Plugin.RearmerCache.GetOrCreateValue(__instance);
                        data.IsManaged = true;
                        data.CheckInterval = config.CheckInterval.Value;
                        data.Radius = config.Radius.Value;
                        data.UnitCooldown = config.UnitCooldown.Value;

                        var traverse = Traverse.Create(__instance);
                        traverse.Field("singleUse").SetValue(!config.Replenishable.Value);
                        traverse.Field("range").SetValue(config.Radius.Value);
                        
                        break; 
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[NavalBuffetMod] Error in Rearmer Start Patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Rearmer), "RearmingCheck")]
    public class Rearmer_RearmingCheck_Patch
    {
        static bool Prefix(Rearmer __instance)
        {
            try
            {
                if (__instance == null) return true;

                if (Plugin.RearmerCache.TryGetValue(__instance, out Plugin.RearmerData data) && data.IsManaged)
                {
                    float currentTime = Time.time;
                    
                    if (currentTime - data.LastCheckTime < data.CheckInterval)
                    {
                        return false; 
                    }

                    data.LastCheckTime = currentTime;

                    
                    data.CurrentHits.Clear();
                    int hitCount = Physics.OverlapSphereNonAlloc(__instance.transform.position, data.Radius, data.HitBuffer, Physics.AllLayers, QueryTriggerInteraction.Ignore);
                    
                    
                    for (int i = 0; i < hitCount; i++)
                    {
                        Collider hit = data.HitBuffer[i];
                        if (hit == null) continue;

                        Unit unit = hit.GetComponentInParent<Unit>();
                        if (unit != null)
                        {
                            // Check if unit is on cooldown
                            if (Plugin.UnitLastReplenishTime.TryGetValue(unit, out var lastTimeBox))
                            {
                                if (currentTime - lastTimeBox.Value < data.UnitCooldown)
                                {
                                    continue; // Unit is still on cooldown
                                }
                            }
                        }
                        
                        data.CurrentHits.Add(hit);

                        if (!data.KnownColliders.Contains(hit))
                        {
                            __instance.gameObject.SendMessage("OnTriggerEnter", hit, SendMessageOptions.DontRequireReceiver);
                            data.KnownColliders.Add(hit);
                        }
                        else
                        {
                            __instance.gameObject.SendMessage("OnTriggerStay", hit, SendMessageOptions.DontRequireReceiver);
                        }
                    }

                    data.KnownColliders.RemoveWhere(known => 
                    {
                        if (known == null || !data.CurrentHits.Contains(known))
                        {
                            if (known != null)
                            {
                                __instance.gameObject.SendMessage("OnTriggerExit", known, SendMessageOptions.DontRequireReceiver);
                            }
                            return true; // Removes from KnownColliders
                        }
                        return false;
                    });

                    return true;
                }

                return true;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(Ship), "CanRearm")]
    public class Ship_CanRearm_Patch
    {
        static void Postfix(Ship __instance, ref bool __result)
        {
            if (__result) return;

            try
            {
                // If it's a ship and the base game says it doesn't need rearming, double check the weapon stations.
                // This addresses the "Land Attack Missile" reload exception where the ship doesn't realize it's empty.
                foreach (var ws in __instance.weaponStations)
                {
                    if (ws != null && ws.Ammo < ws.FullAmmo)
                    {
                        // Plugin.Log.LogInfo($"[NavalBuffetMod] Ship '{__instance.unitName}' has empty weapon station ({ws.WeaponInfo?.name ?? "Unknown"}). Forcing CanRearm = true.");
                        __result = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[NavalBuffetMod] Error in Ship.CanRearm Patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Ship), "Rearm")]
    public class Ship_Rearm_Timer_Patch
    {
        static void Postfix(Ship __instance)
        {
            Plugin.UnitLastReplenishTime.GetOrCreateValue(__instance).Value = Time.time;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "Rearm")]
    public class Aircraft_Rearm_Timer_Patch
    {
        static void Postfix(Aircraft __instance)
        {
            Plugin.UnitLastReplenishTime.GetOrCreateValue(__instance).Value = Time.time;
        }
    }

    [HarmonyPatch(typeof(GroundVehicle), "Rearm")]
    public class GroundVehicle_Rearm_Timer_Patch
    {
        static void Postfix(GroundVehicle __instance)
        {
            Plugin.UnitLastReplenishTime.GetOrCreateValue(__instance).Value = Time.time;
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.user.navalbuffetmod";
        public const string PLUGIN_NAME = "NavalBuffetMod";
        public const string PLUGIN_VERSION = "1.6.0";
    }
}