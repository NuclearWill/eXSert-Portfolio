/*
 * Written by: Will T
 * 
 * ProgressionZone is an abstract base class for any trigger zones.
 * It automatically communicates with the ProgressionManager within the scene to register itself in its database.
 * 
 */

using UnityEngine;

namespace Progression
{
    [RequireComponent(typeof(BoxCollider))]
    [HelpURL("https://docs.google.com/document/d/18pi24ZJ65GG307F6SvKpSoHPs0izxSb6yZ6cfjvYqMQ/edit?pli=1&tab=t.0#bookmark=id.b9oi5t9la060")]
    [DefaultExecutionOrder(10)] // Ensure this executes after the ProgressionManager, which may rely on it to register itself in Awake
    public abstract class ProgressionZone : MonoBehaviour
    {
        #region Inspector Setup
        [Header("Progression Zone Settings")]
        [SerializeField, Tooltip("Whether the zone is enabled at the start of the scene. If false, the player will not trigger the zone until it is enabled by another encounter.")]
        private bool startEnabled = true;

        [SerializeField]
        private bool SendDebugMessages = false;

        [ContextMenu("Find Progression Manager")]
        void FindManager()
        {
#if UNITY_EDITOR
            var manager = FindAnyObjectByType<ProgressionManager>();
            if (manager != null)
            {
                UnityEditor.Selection.activeGameObject = manager.gameObject;
                UnityEditor.EditorGUIUtility.PingObject(manager.gameObject);

                if (debugMessagesEnabled)
                    Debug.Log($"ProgressionManager found on '{manager.gameObject.name}' and selected in the editor.");
            }
            else
            {
                Debug.LogWarning("ProgressionManager not found in the scene.");
            }
#else
            Debug.LogWarning("FindManager is an editor-only helper and cannot run in a build.");
#endif
        }
        #endregion

        protected BoxCollider progressionCollider;

        protected bool debugMessagesEnabled => SendDebugMessages;

        /// <summary>
        /// Indicates if the encounter can be started when the player is in the zone
        /// </summary>
        protected bool zoneEnabled = true;

        /// <summary>
        /// Indicates whether the player is currently within the encounter zone
        /// </summary>
        protected bool zoneActive = false;

        protected virtual void Awake()
        {
            // Ensures the BoxCollider is set up properly as a trigger volume.
            // Additionally caches it for later use.

            progressionCollider = GetComponent<BoxCollider>();

            if (progressionCollider == null)
                Debug.LogError("ProgressionZone requires a BoxCollider component.");
            else
                progressionCollider.isTrigger = true;
        }

        protected virtual void Start()
        {
            // Registers the encounter with the progression manager and also sets its initial state from its settings.

            AddToManager();

            if (!startEnabled) DisableZone();
            else EnableZone();
        }

        private void AddToManager() => ProgressionManager.AddProgressable(this);

        /// <summary>
        /// Enables the encounter zone to be triggerable, allowing the player to now walk into the zone and start the encounter.
        /// If the player is already within the confines of the zone, it will start immediately.
        /// </summary>
        public void EnableZone()
        {
            zoneEnabled = true;
            if (progressionCollider == null)
            {
                progressionCollider = GetComponent<BoxCollider>();
            }
            if (progressionCollider == null)
            {
                Debug.LogError($"[{GetType()}]Cannot enable zone because the BoxCollider component is missing.");
                return;
            }
            UpdateCollider();

            // If the player is already in the zone when it gets enabled, manually trigger the enter logic since OnTriggerEnter won't be called until they exit and re-enter.
            if (zoneActive) PlayerEnteredZone();
        }

        /// <summary>
        /// Disables the encounter zone to prevent the player from triggering it.
        /// </summary>
        public void DisableZone()
        {
            zoneEnabled = false;
            if (progressionCollider == null)
            {
                progressionCollider = GetComponent<BoxCollider>();
            }
            if (progressionCollider == null)
            {
                Debug.LogError($"[{GetType()}] Cannot enable zone because the BoxCollider component is missing.");
                return;
            }
            UpdateCollider();
        }

        private void UpdateCollider() => progressionCollider.enabled = zoneEnabled;

        #region Collider Triggers
        protected void OnTriggerEnter(Collider other)
        {
            if (!other.transform.root.CompareTag("Player")) return;
            zoneActive = true;

            if (!zoneEnabled) return;
            PlayerEnteredZone();
        }
        protected void OnTriggerExit(Collider other)
        {
            if (!other.transform.root.CompareTag("Player")) return;
            zoneActive = false;

            if (!zoneEnabled) return;
            PlayerExitedZone();
        }

        /// <summary>
        /// Class specific functionality for when the player enters the encounter zone.
        /// Automatically called when the player enters the trigger volume when its enabled.
        /// </summary>
        protected abstract void PlayerEnteredZone();

        /// <summary>
        /// Class specific functionality for when the player exits the encounter zone.
        /// Automatically called when the player exits the trigger volume when its enabled.
        /// </summary>
        protected abstract void PlayerExitedZone();
        #endregion

        #region Debug Scripts
        protected abstract Color DebugColor { get; }

        protected virtual void OnDrawGizmos()
        {
            // Draws a wireframe box to help visualize the confines of the encounter zone in the editor.

            if (progressionCollider == null)
                progressionCollider = GetComponent<BoxCollider>();

            Gizmos.color = DebugColor;
            Gizmos.DrawWireCube(progressionCollider.bounds.center, progressionCollider.bounds.size);
        }
        #endregion
    }
}
