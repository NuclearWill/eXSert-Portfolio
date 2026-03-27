/*
 * Written by: Will T
 * 
 * A basic script which just handles the tutorial progression in the elevator scene.
 * Designed purely for the tutorial within the elevator sequence, not intended to be used anywhere else.
 */

using Progression.Encounters;
using UIandUXSystems.HUD;
using UnityEngine;
using Managers.TimeLord;
using System.Collections;

public class TutorialHandler : MonoBehaviour
{
    #region Inspector Setup
    [SerializeField] private HUDMessage initialMessage;
    [SerializeField, CriticalReference] 
    private NavigationEntryInteraction tutorialEntry;
    [SerializeField] private HUDMessage postEntryMessage;
    [SerializeField, CriticalReference] private CombatEncounter singleTargetFight;
    [SerializeField] private CombatEncounter aoeTargetFight;
    #endregion

    private void Start()
    {
        PlayerHUD.NewMessage(initialMessage);
    }

    private void OnEnable()
    {
        tutorialEntry.OnEntryCollected += OnEntryCollected;
        tutorialEntry.OnEntryRead += OnEntryRead;
    }

    private void OnDisable()
    {
        tutorialEntry.OnEntryCollected -= OnEntryCollected;
        tutorialEntry.OnEntryRead -= OnEntryRead;
    }

    private void OnEntryCollected(string entryId)
    {
        Debug.Log($"[TutorialHandler] Entry with id {entryId} collected. Updating tutorial progression.");
        PlayerHUD.NewMessage(postEntryMessage);
    }

    // Specifically waits for the game to resume before enabling the fight.
    // Doing it while game is paused breaks everything for SOME REASON
    private void OnEntryRead()
    {
        PauseCoordinator.OnResumed += PauseCoordinator_OnResumed;

        void PauseCoordinator_OnResumed()
        {
            PauseCoordinator.OnResumed -= PauseCoordinator_OnResumed;

            // Guard against the encounter having been destroyed.
            if (singleTargetFight == null)
            {
                Debug.LogWarning("[TutorialHandler] singleTargetFight reference is null or has been destroyed. Cannot enable encounter.");
                
            }

            // Defer to next frame to avoid race conditions with Unity's resume lifecycle.
            StartCoroutine(EnableEncounterNextFrame(singleTargetFight));
        }
    }

    private IEnumerator EnableEncounterNextFrame(CombatEncounter encounter)
    {
        yield return null;

        if (encounter == null)
        {
            Debug.LogWarning("[TutorialHandler] Encounter became null before it could be enabled.");
            yield break;
        }

        // Final defensive check: ensure the encounter's GameObject still exists.
        if (encounter.gameObject == null)
        {
            Debug.LogWarning("[TutorialHandler] Encounter GameObject is missing/destroyed. Aborting enable.");
            yield break;
        }

        encounter.EnableZone();
    }
}
