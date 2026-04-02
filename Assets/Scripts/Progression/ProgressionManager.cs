/* 
 * Written by Will T
 * 
 * The ProgressionManager is responsible for managing the state of all progression related systems within a specific scene.
 * They use a special version of singleton pattern (SceneSingleton) to allow for multiple independent managers across each scene.
 * Anything that inherits from ProgressionZone will automatically register itself with the manager in its scene.
 * 
 * Special functionality was added so that if a designer plays a level scene by itself.
 * The manager will automatically load the player scene and set up the player at the first checkpoint to allow for easier testing.
 */

using Singletons;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UIandUXSystems.HUD;

namespace Progression
{
    using Encounters;
    using Checkpoints;
    using SceneManagement;

    [HelpURL("https://docs.google.com/document/d/18pi24ZJ65GG307F6SvKpSoHPs0izxSb6yZ6cfjvYqMQ/edit?pli=1&tab=t.0#bookmark=id.ba7p6f215mok")]
    [DefaultExecutionOrder(0)] // Ensure this executes before any encounters or progression zones, which may rely on it to register themselves in Awake
    public class ProgressionManager : SceneSingleton<ProgressionManager>
    {
        #region Inspector Setup
        [Header("Progression Settings")]
        [Header("Prewarming")]
        [SerializeField, Tooltip("If true, the manager will automatically derive prewarm counts from combat encounter waves in this scene.")]
        private bool usePrewarmer = false;
        [SerializeField, Tooltip("Optional extra prefabs or spawn markers to include in prewarming on top of the automatically detected combat encounter requirements.")]
        private GameObject[] prefabsToPrewarm;

        [Header("Checkpoint Settings")]
        [SerializeField, CriticalReference, Tooltip("Checkpoint to use as the initial spawn point for this scene. Assign one of the scene's CheckpointBehavior objects.")]
        private CheckpointBehavior firstCheckpoint;
        #endregion

#if UNITY_EDITOR
        /*
         * This variable determines if the scene is being loaded by itself in the editor.
         * (Hitting play mode while just the scene is active)
         * 
         * If true, the manager will automatically load the player scene.
         * It will also move the player to the first checkpoint's spawn point.
         */
private static bool IsolatedLoad = true; // If the scene is being loaded by itself or not
        private string editorBootstrapLoadingScreenSuppressionId;
#endif

        /// <summary>
        /// Indicates whether all encounters in the scene have been completed
        /// </summary>
        private int totalEncountersInScene = 0;

        private readonly List<BasicEncounter> encounterCompletionMap = new();
        private readonly List<SceneLoadZone> loadZones = new();
        private readonly List<CheckpointBehavior> checkpoints = new();

        /// <summary>
        /// Public accessor for the configured first checkpoint.
        /// At runtime the checkpoints list will be populated during Awake; this returns the serialized selection.
        /// </summary>
        public CheckpointBehavior FirstCheckpoint => firstCheckpoint;

        #region Monobehavior Methods
        protected override void Awake()
        {
            base.Awake(); // Scene Singleton behavior

#if UNITY_EDITOR
            if (ShouldSuppressLoadingScreenForEditorBootstrap())
                editorBootstrapLoadingScreenSuppressionId = SceneLoader.RequestLoadingScreenSuppression(SceneLoader.EditorBootstrapLoadingScreenOwnerId);
#endif

            this.gameObject.name = $"[{SceneAsset.GetSceneAssetOfObject(this.gameObject)}] Progression Manager";

            encounterCompletionMap.Clear();
            loadZones.Clear();
            checkpoints.Clear();

            CheckpointBehavior.OverrideCurrentCheckpoint(firstCheckpoint, overrideIfNull: false);

            if (usePrewarmer) PrewarmEnemies();
        }

        private void Start()
        {
#if UNITY_EDITOR
            // Automatically load the player scene while playing the level in editor.
            // This makes testing easier since you can just hit play on the level scene by itself
            // Without needing to manually adjust the player scene
            if (ShouldSuppressLoadingScreenForEditorBootstrap())
            {
                // Ensure the SceneLoader is initialized so that it can properly load the player scene
                SceneLoader.Initialize();
                AsyncOperation playerLoad = SceneLoader.LoadPlayerScene();

                if (playerLoad == null) ReleaseEditorBootstrapLoadingScreenSuppression();
                
                else
                {
                    // When the player scene completes loading, spawn the player at the first checkpoint.
                    playerLoad.completed += _ =>
                    {
                        Player.SpawnPlayerAtCheckpoint();
                        PlayerHealthBarManager.Instance?.RestoreDesignTimeDefaults();
                        StartCoroutine(ReleaseEditorBootstrapLoadingScreenSuppressionNextFrame());
                    };
                }
            }
            IsolatedLoad = false;
#endif
        }

#if UNITY_EDITOR
        /*
         * These small methods and there specific implementation were added by another programmer.
         * The goal was to remove the loading screen while in editor mode to set up time.
         * 
         * The basic idea of automatically loading the player scene when loading the level scene was my idea and I orginally implemented it.
         */

        private bool ShouldSuppressLoadingScreenForEditorBootstrap()
        {
            return IsolatedLoad && SceneAsset.LoadedSceneCount == 1 && !SceneAsset.PlayerLoaded;
        }

        private IEnumerator ReleaseEditorBootstrapLoadingScreenSuppressionNextFrame()
        {
            yield return null;
            ReleaseEditorBootstrapLoadingScreenSuppression();
        }

        private void ReleaseEditorBootstrapLoadingScreenSuppression()
        {
            if (string.IsNullOrWhiteSpace(editorBootstrapLoadingScreenSuppressionId))
                return;

            SceneLoader.ReleaseLoadingScreenSuppression(editorBootstrapLoadingScreenSuppressionId);
            editorBootstrapLoadingScreenSuppressionId = null;
        }
#endif

        private void OnDisable()
        {
#if UNITY_EDITOR
            ReleaseEditorBootstrapLoadingScreenSuppression();
#endif

            foreach (BasicEncounter encounter in encounterCompletionMap)
            {
                if (encounter != null && !encounter.isCleanedUp)
                    encounter.ManualCleanUpCall();
            }

            encounterCompletionMap.Clear();
        }
        #endregion

        private void UpdateObjective(HUDMessage message) => PlayerHUD.NewMessage(message);

        #region Progression Management
        /// <summary>
        /// Adds the encounter to the manager's database.
        /// </summary>
        /// <param name="encounter"></param>
        internal static void AddProgressable(ProgressionZone zone)
        {
            // Get the manager for this zone's scene and add the zone to the appropriate list
            ProgressionManager manager = GetInstance(SceneAsset.GetSceneAssetOfObject(zone.gameObject));

            // Add the zone to the appropriate list based on its type.
            // Then perform any necessary setup for the manager to track it
            switch (zone)
            {
                case BasicEncounter encounter:
                    manager.encounterCompletionMap.Add(encounter);
                    manager.totalEncountersInScene++;

                    // Subscribe the manager to the zone's UpdateObjective event so it can relay updates to the HUD
                    encounter.UpdateObjective += manager.UpdateObjective;
                    break;

                case SceneLoadZone loadZone:
                    manager.loadZones.Add(loadZone);
                    break;

                case CheckpointBehavior checkpoint:
                    manager.checkpoints.Add(checkpoint);
                    break;

                default:
                    // No action for other ProgressionZone types (preserve original behavior)
                    Debug.LogWarning($"[ProgressionManager] Added ProgressionZone {zone.gameObject.name} of type {zone.GetType()} that is not explicitly handled by AddProgressable. No action taken.");
                    break;
            }
        }
        #endregion

        #region Prewarming Logic
        /*
         * I implemented the enemy object pooler and its prewarming functions.
         * I added a simple manual prewarming request for the progression manager.
         * 
         * However, that has since been replaced by another programmer with the ability for the manager to automatically determine its prewarming count.
         * All functionality present below in this specific script are not my work.
         */

        private void PrewarmEnemies()
        {
            Dictionary<GameObject, int> shoppingList = new();

            AddAutomaticEncounterPrewarmCounts(shoppingList);
            AddManualPrewarmCounts(shoppingList);

            if (shoppingList.Count == 0)
            {
                Debug.LogWarning($"[ProgressionManager] No enemy prefabs were found to prewarm in scene '{gameObject.scene.name}'.");
                return;
            }

            foreach (KeyValuePair<GameObject, int> entry in shoppingList)
            {
                EnemyFactory.Prewarm(entry.Key, entry.Value);
            }

            Debug.Log($"[ProgressionManager] Auto-prewarmed {shoppingList.Count} enemy prefab type(s) across {totalEncountersInScene} combat encounter(s) in scene '{gameObject.scene.name}'.");
        }

        private void AddAutomaticEncounterPrewarmCounts(Dictionary<GameObject, int> shoppingList)
        {
            int combatEncounterCount = 0;

            foreach (CombatEncounter encounter in GetSceneComponents<CombatEncounter>())
            {
                if (encounter == null)
                    continue;

                combatEncounterCount++;
                AddEncounterPrewarmCounts(encounter, shoppingList);
            }

            totalEncountersInScene = combatEncounterCount;
        }

        private void AddEncounterPrewarmCounts(CombatEncounter encounter, Dictionary<GameObject, int> shoppingList)
        {
            foreach (Transform child in encounter.transform)
            {
                if (!child.name.ToLower().Contains("wave") || !child.gameObject.activeSelf)
                    continue;

                AddWavePrewarmCounts(child, shoppingList);
            }
        }

        private void AddWavePrewarmCounts(Transform waveRoot, Dictionary<GameObject, int> shoppingList)
        {
            Dictionary<GameObject, int> waveCounts = new();

            foreach (Transform waveChild in waveRoot)
            {
                if (!waveChild.TryGetComponent<EnemySpawnMarker>(out EnemySpawnMarker marker))
                    continue;

                GameObject intendedPrefab = ResolvePrewarmPrefab(marker.gameObject);
                if (intendedPrefab == null)
                    continue;

                if (!waveCounts.ContainsKey(intendedPrefab))
                    waveCounts[intendedPrefab] = 1;
                else
                    waveCounts[intendedPrefab]++;
            }

            foreach (KeyValuePair<GameObject, int> entry in waveCounts)
            {
                RegisterPrewarmCount(shoppingList, entry.Key, entry.Value);
            }
        }

        private void AddManualPrewarmCounts(Dictionary<GameObject, int> shoppingList)
        {
            Dictionary<GameObject, int> manualCounts = new();

            // Create a shopping list of prefabs to prewarm and their quantities
            foreach (GameObject prefab in prefabsToPrewarm)
            {
                if (prefab == null) continue;

                GameObject intendedPrefab = ResolvePrewarmPrefab(prefab);
                if (intendedPrefab == null)
                    continue;

                if (!manualCounts.ContainsKey(intendedPrefab))
                    manualCounts[intendedPrefab] = 1;
                else
                    manualCounts[intendedPrefab]++;
            }

            foreach (KeyValuePair<GameObject, int> entry in manualCounts)
            {
                RegisterPrewarmCount(shoppingList, entry.Key, entry.Value);
            }
        }

        private GameObject ResolvePrewarmPrefab(GameObject prefabOrMarker)
        {
            if (prefabOrMarker == null)
                return null;

            GameObject intendedPrefab;

            if (prefabOrMarker.TryGetComponent<EnemySpawnMarker>(out EnemySpawnMarker marker))
            {
                intendedPrefab = marker.EnemyPrefab;

                if (intendedPrefab == null)
                {
                    Debug.LogWarning($"[ProgressionManager] Spawn marker '{prefabOrMarker.name}' does not have an enemy prefab assigned and will be skipped in prewarming.");
                    return null;
                }
            }
            else
            {
                intendedPrefab = prefabOrMarker;
            }

            if (intendedPrefab.GetComponentInChildren<BaseEnemyCore>(includeInactive: true) == null)
            {
                Debug.LogWarning($"[ProgressionManager] Prefab {intendedPrefab.name} does not have a BaseEnemyCore component and will be skipped in prewarming.");
                return null;
            }

            return intendedPrefab;
        }

        private static void RegisterPrewarmCount(Dictionary<GameObject, int> shoppingList, GameObject prefab, int count)
        {
            if (prefab == null || count <= 0)
                return;

            if (!shoppingList.TryGetValue(prefab, out int existingCount) || count > existingCount)
                shoppingList[prefab] = count;
        }

        private IEnumerable<T> GetSceneComponents<T>() where T : Component
        {
            foreach (GameObject rootObject in gameObject.scene.GetRootGameObjects())
            {
                T[] components = rootObject.GetComponentsInChildren<T>(true);
                foreach (T component in components)
                    yield return component;
            }
        }
        #endregion
    }
}