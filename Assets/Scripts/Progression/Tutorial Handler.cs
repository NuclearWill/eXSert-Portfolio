/*
 * Written by: Will T
 * 
 * A basic script which just handles the tutorial progression in the elevator scene.
 * Designed purely for the tutorial within the elevator sequence, not currently intended to be used anywhere else.
 *
 * Uses event Action subscriptions to monitor when key tutorial milestones are met.
 * Events use gates to ensure that events dont trigger tutorial progression out of order or multiple times.
 */

using Managers.TimeLord;
using Progression.Encounters;
using System.Collections;
using UIandUXSystems.HUD;
using UnityEngine;
using Utilities.Combat.Attacks;

public class TutorialHandler : MonoBehaviour
{
    #region Inspector Setup
    [Header("Objective Messages")]
    [SerializeField] private HUDMessage initialMessage;
    [SerializeField] private HUDMessage singleTargetFightMessage;
    [SerializeField] private HUDMessage aoeTargetFightMessage;
    [SerializeField] private HUDMessage correctButtonPressedMessage;
    [SerializeField] private HUDMessage tutorialCompleteMessage;

    [Header("Tutorial Progression References")]
    [SerializeField, CriticalReference] 
    private NavigationEntryInteraction tutorialEntry;
    // [SerializeField] private HUDMessage postEntryMessage;
    [SerializeField, CriticalReference] private CombatEncounter singleTargetFight;
    [SerializeField, CriticalReference] private CombatEncounter aoeTargetFight;
    [SerializeField, CriticalReference] private GameObject keycardToEnable;
    [SerializeField, CriticalReference] private SceneAsset nextScene;
    #endregion

    private bool logCollected = false;
    private bool correctButtonPressed;
    private bool FirstFightCompleted => singleTargetFight.isCompleted;
    private bool SecondFightCompleted => aoeTargetFight.isCompleted;

    #region Couroutines
    private Coroutine enableRetryRoutine;
    private const float EncounterRetryInterval = 3f;

    // Monitor for destruction/tracking
    private Coroutine destroyMonitorRoutine;
    private const float DestroyMonitorInterval = 1f; // seconds between checks
    private bool wasDestroyedState = false;
    #endregion

    private void Start()
    {
        keycardToEnable.SetActive(false); // Ensures the keycard is disabled at the start of the tutorial
        PlayerHUD.NewMessage(initialMessage);
    }

    private void OnEnable()
    {
        tutorialEntry.OnEntryCollected += OnEntryCollected;

        // Subscribe to PlayerAttackManager attack-type events
        PlayerAttackManager.OnSingleAttack += OnPlayerAttack;
        PlayerAttackManager.OnAoeAttack += OnPlayerAttack;

        // Subscribe to encounter completion events if they aren't already completed
        singleTargetFight.OnEncounterCompleted += OnEncounterCompleted;
        aoeTargetFight.OnEncounterCompleted += OnEncounterCompleted;
    }

    private void OnDisable()
    {
        tutorialEntry.OnEntryCollected -= OnEntryCollected;

        // Unsubscribe from PlayerAttackManager events
        PlayerAttackManager.OnSingleAttack -= OnPlayerAttack;
        PlayerAttackManager.OnAoeAttack -= OnPlayerAttack;

        // Unsubscribe from encounter completion events
        singleTargetFight.OnEncounterCompleted -= OnEncounterCompleted;
        aoeTargetFight.OnEncounterCompleted -= OnEncounterCompleted;

        if (enableRetryRoutine != null)
        {
            StopCoroutine(enableRetryRoutine);
            enableRetryRoutine = null;
        }

        if (destroyMonitorRoutine != null)
        {
            Debug.Log("[TutorialHandler] Stopping destroy monitor coroutine on disable.");
            StopCoroutine(destroyMonitorRoutine);
            destroyMonitorRoutine = null;
        }
    }

    private void OnEntryCollected(string entryId)
    {
        logCollected = true;

        StartCombatTutorial(singleTargetFight, singleTargetFightMessage);
    }

    #region Combat Tutorial Handlers
    private void StartCombatTutorial(CombatEncounter fight, HUDMessage message)
    {
        Debug.Log($"[TutorialHandler] Starting combat tutorial for encounter {fight.name}. Displaying message and enabling fight zone.");
        correctButtonPressed = false; // Resets the button press requirement for this part of the tutorial
        PlayerHUD.NewMessage(message); // Displays the appropriate message for the current fight
        fight.EnableZone(); // Enables the fight zone
    }

    private void OnPlayerAttack(PlayerAttack attack)
    {
        AttackType type = attack.attackType;

        bool shouldProcess = 
            (type == AttackType.LightSingle && logCollected) || 
            (type == AttackType.HeavyAOE && FirstFightCompleted);

        if (!shouldProcess) return;

        // Unsubscribe from the specific attack event to prevent multiple triggers for the same attack type.
        switch (type)
        {
            case AttackType.LightSingle: PlayerAttackManager.OnSingleAttack -= OnPlayerAttack; break;
            case AttackType.HeavyAOE: PlayerAttackManager.OnAoeAttack -= OnPlayerAttack; break;
            default: Debug.LogWarning($"[TutorialHandler] Received unexpected attack type {type}. No event unsubscription performed."); break;
        }

        Debug.Log($"[TutorialHandler] Player performed attack of type {type}. Updating Progress...");

        PlayerHUD.NewMessage(correctButtonPressedMessage);
        
        correctButtonPressed = true; // Updates tutorial progress

        // Checks if the second fight is complete to mark the tutorial as complete
        if (SecondFightCompleted) TutorialComplete();

        // If the second fight isn't complete, checks if the first fight is complete to start the second
        else if (type == AttackType.LightSingle && FirstFightCompleted) 
            StartCombatTutorial(aoeTargetFight, aoeTargetFightMessage);
    }

    private void OnEncounterCompleted()
    {
        Debug.Log($"[TutorialHandler] Encounter completed called. Checking conditions for tutorial progression...");
        if (!correctButtonPressed) return; // Only proceed if the correct button was pressed

        // Checks which fight was completed and updates the tutorial progression accordingly.
        if (SecondFightCompleted) 
        {
            Debug.Log($"[TutorialHandler] Second encounter completed. Updating tutorial progress...");

            TutorialComplete();

            return;
        }

        // The first fight was completed
        Debug.Log($"[TutorialHandler] First encounter completed. Updating tutorial progress...");

        correctButtonPressed = false; // Resets the button press requirement for the next part of the tutorial
        
        StartCombatTutorial(aoeTargetFight, aoeTargetFightMessage);
    }
    #endregion

    private void TutorialComplete()
    {
        Debug.Log($"[TutorialHandler] Tutorial complete! All conditions met.");

        keycardToEnable.SetActive(true); // Enables the keycard to allow progression to the next scene

        PlayerHUD.NewMessage(tutorialCompleteMessage); // Displays the tutorial complete message

        SceneLoader.Load(nextScene, loadScreen: false); // Loads the next scene for the player
    }
}
