using System;
using System.Collections.Generic;
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
        }

        public static Dictionary<string, ContainerConfig> Configs = new Dictionary<string, ContainerConfig>();

        private void Awake()
        {
            Log = Logger;
            
            string[] targets = { "MunitionsContainer1", "MunitionsPallet1", "NavalSupplyContainer1", "NavalPallet1" };
            
            foreach (var target in targets)
            {
                Configs[target] = new ContainerConfig
                {
                    Replenishable = Config.Bind(target, "Replenishable", true, "Whether the container can be used multiple times without despawning."),
                    Radius = Config.Bind(target, "Radius", 1000f, "Resupply radius.")
                };
            }

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            
            Log.LogInfo("Harmony patches applied successfully. Configuration initialized.");
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
                        Plugin.Log.LogInfo($"[NavalBuffetMod] New Rearmer detected: '{objName}'. Initializing...");
                        // We rely on RearmingCheck to apply the initial config values.
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
        static void Prefix(Rearmer __instance)
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
                        var traverse = Traverse.Create(__instance);
                        
                        float currentRange = traverse.Field<float>("range").Value;
                        bool currentSingleUse = traverse.Field<bool>("singleUse").Value;
                        bool targetSingleUse = !config.Replenishable.Value;

                        // PERFORMANCE OPTIMIZATION: Only perform heavy physical updates if the configuration has changed
                        // or if this is the first time we're applying it.
                        if (Mathf.Abs(currentRange - config.Radius.Value) > 0.1f || currentSingleUse != targetSingleUse)
                        {
                            Plugin.Log.LogInfo($"[NavalBuffetMod] Updating {objName}: Radius {config.Radius.Value}, Replenishable {config.Replenishable.Value}");

                            traverse.Field("singleUse").SetValue(targetSingleUse);
                            traverse.Field("range").SetValue(config.Radius.Value);

                            foreach (var collider in __instance.GetComponentsInChildren<Collider>(true))
                            {
                                if (collider.isTrigger)
                                {
                                    // FIX: Collateral Layer Damage
                                    // Only move to 'Ignore Raycast' (Layer 2) if the GameObject doesn't have physical colliders
                                    bool hasPhysicalCollider = false;
                                    foreach (var c in collider.gameObject.GetComponents<Collider>())
                                    {
                                        if (!c.isTrigger) { hasPhysicalCollider = true; break; }
                                    }

                                    if (!hasPhysicalCollider)
                                    {
                                        collider.gameObject.layer = 2;
                                    }

                                    if (collider is SphereCollider sc) sc.radius = config.Radius.Value;
                                    else if (collider is CapsuleCollider cc) cc.radius = config.Radius.Value;
                                    else if (collider is BoxCollider bc) bc.size = new Vector3(config.Radius.Value * 2, config.Radius.Value * 2, config.Radius.Value * 2);
                                }
                            }
                        }
                        
                        break; 
                    }
                }
            }
            catch (Exception ex)
            {
                // Never leave this empty! If something breaks, you want to know about it.
                Plugin.Log.LogError($"[NavalBuffetMod] Error in Rearmer RearmingCheck Patch: {ex}");
            }
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.user.navalbuffetmod";
        public const string PLUGIN_NAME = "NavalBuffetMod";
        public const string PLUGIN_VERSION = "1.3.2";
    }
}
