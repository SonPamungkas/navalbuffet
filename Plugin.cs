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
                string matchedTarget = null;
                
                foreach (var target in Plugin.Configs.Keys)
                {
                    if (objName.Contains(target))
                    {
                        matchedTarget = target;
                        break;
                    }
                }

                if (matchedTarget != null)
                {
                    Plugin.Log.LogInfo($"[Wiretap] Found TARGET Rearmer on GameObject: '{objName}' | Initial config will be applied.");
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
                string matchedTarget = null;
                
                foreach (var target in Plugin.Configs.Keys)
                {
                    if (objName.Contains(target))
                    {
                        matchedTarget = target;
                        break;
                    }
                }

                if (matchedTarget != null)
                {
                    var traverse = Traverse.Create(__instance);
                    var config = Plugin.Configs[matchedTarget];
                    
                    traverse.Field("singleUse").SetValue(!config.Replenishable.Value);
                    traverse.Field("range").SetValue(config.Radius.Value);
                    
                    // Update all physical trigger colliders in the object hierarchy
                    foreach (var collider in __instance.GetComponentsInChildren<Collider>(true))
                    {
                        if (collider.isTrigger)
                        {
                            if (collider is SphereCollider sc)
                            {
                                sc.radius = config.Radius.Value;
                            }
                            else if (collider is CapsuleCollider cc)
                            {
                                cc.radius = config.Radius.Value;
                            }
                            else if (collider is BoxCollider bc)
                            {
                                // If it's a box collider trigger, scale the size to encompass the radius
                                bc.size = new Vector3(config.Radius.Value * 2, config.Radius.Value * 2, config.Radius.Value * 2);
                            }
                        }
                    }
                }
            }
            catch (Exception) { }
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.user.navalbuffetmod";
        public const string PLUGIN_NAME = "NavalBuffetMod";
        public const string PLUGIN_VERSION = "1.2.0";
    }
}
