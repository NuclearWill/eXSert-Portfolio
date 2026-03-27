/*
 * Written by: Will T
 * 
 * A basic script which just handles the tutorial progression in the elevator scene.
 * Designed purely for the tutorial within the elevator sequence, not intended to be used anywhere else.
 */

using Progression.Encounters;
using UIandUXSystems.HUD;
using UnityEngine;

public class TutorialHandler : MonoBehaviour
{
    #region Inspector Setup
    [SerializeField] private HUDMessage initialMessage;
    [SerializeField, CriticalReference] 
    private NavigationEntryInteraction tutorialEntry;
    [SerializeField] private HUDMessage postEntryMessage;
    [SerializeField] private CombatEncounter singleTargetFight;
    [SerializeField] private CombatEncounter aoeTargetFight;
    #endregion

    private void Start()
    {
        PlayerHUD.NewMessage(initialMessage);
    }

    private void OnEnable()
    {
        tutorialEntry.OnEntryCollected += OnEntryCollected;
    }

    private void OnDisable()
    {
        tutorialEntry.OnEntryCollected -= OnEntryCollected;
    }

    private void OnEntryCollected(string entryId)
    {
        Debug.Log($"[TutorialHandler] Entry with id {entryId} collected. Updating tutorial progression.");
        PlayerHUD.NewMessage(postEntryMessage);
    }
}
