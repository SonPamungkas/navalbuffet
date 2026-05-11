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
            public ConfigEntry<int> TriggerLayer;
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
                    Radius = Config.Bind(target, "Radius", 1000f, "Resupply radius."),
                    TriggerLayer = Config.Bind(target, "TriggerLayer", 1, "Physics layer for the expanded trigger. 1 = TransparentFX, 2 = IgnoreRaycast, 4 = Water.")
                };
            }

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            
            Log.LogInfo("NavalBuffetMod initialized. Memory leaks patched.");
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
                        Plugin.Log.LogInfo($"[NavalBuffetMod] Applying optimized config to spawned Rearmer: '{objName}'");
                        var traverse = Traverse.Create(__instance);
                        traverse.Field("singleUse").SetValue(!config.Replenishable.Value);
                        traverse.Field("range").SetValue(config.Radius.Value);
                        foreach (var collider in __instance.GetComponentsInChildren<Collider>(true))
                        {
                            if (collider.isTrigger)
                            {
                                // Apply the layer from config to prevent terrain avoidance without breaking hull detection
                                collider.gameObject.layer = config.TriggerLayer.Value;

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
                                    bc.size = new Vector3(config.Radius.Value * 2, config.Radius.Value * 2, config.Radius.Value * 2);
                                }
                            }
                        }
                        
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


    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.user.navalbuffetmod";
        public const string PLUGIN_NAME = "NavalBuffetMod";
        public const string PLUGIN_VERSION = "1.3.2";
    }
}
