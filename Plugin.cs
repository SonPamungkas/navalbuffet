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
            public ConfigEntry<float> CheckInterval; // Throttle setting
        }

        public static Dictionary<string, ContainerConfig> Configs = new Dictionary<string, ContainerConfig>();

        // Virtual Trigger
        public class RearmerData
        {
            public bool IsManaged = false;
            public float LastCheckTime = 0f;
            public float CheckInterval = 1f;
            public float Radius = 1000f;
            
            // Pre-allocated buffers to prevent Memory Spikes
            public Collider[] HitBuffer = new Collider[500]; 
            public HashSet<Collider> KnownColliders = new HashSet<Collider>();
            public HashSet<Collider> CurrentHits = new HashSet<Collider>();
        }
        public static ConditionalWeakTable<Rearmer, RearmerData> RearmerCache = new ConditionalWeakTable<Rearmer, RearmerData>();

        private void Awake()
        {
            Log = Logger;
            
            string[] targets = { "MunitionsContainer1", "MunitionsPallet1", "NavalSupplyContainer1", "NavalPallet1" };
            
            foreach (var target in targets)
            {
                Configs[target] = new ContainerConfig
                {
                    Replenishable = Config.Bind(target, "Replenishable", true, "Whether the container can be used multiple times."),
                    Radius = Config.Bind(target, "Radius", 1000f, "Resupply radius."),
                    CheckInterval = Config.Bind(target, "CheckInterval", 1.0f, "Throttle (in seconds) to prevent memory crashes.")
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

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.user.navalbuffetmod";
        public const string PLUGIN_NAME = "NavalBuffetMod";
        public const string PLUGIN_VERSION = "1.5.0";
    }
}
