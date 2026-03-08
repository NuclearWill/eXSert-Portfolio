/*
    Written by Brandon Wahl

    Specialized unlockable interaction for doors.
    Place this script on any GameObject that will allow a certain door to open.
    It could be on a console, a button, or even the door itself.
    Make sure to assign the DoorHandler component of the door you want to interact with in the inspector.
*/
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using Unity.Cinemachine;

public class DoorInteractions : UnlockableInteraction
{
    [Tooltip("Place the gameObject with the DoorHandler component here, it may be on a different object or the same object as this script.")]
    [SerializeField] private List<DoorHandler> doorHandlers;

    [Header("Interaction")]
    [SerializeField] private bool onlyInteractableOnce = false;
    [SerializeField] private string lockedInteractionPrompt = "LOCKED";

    [Header("Camera")]
    [SerializeField] private bool usePuzzleCameraOnInteraction = false;
    [SerializeField, Tooltip("Optional Cinemachine camera to use for the puzzle interaction.")]
    private CinemachineCamera puzzleCinemachineCamera;
    [SerializeField, Min(0f)] private float puzzleCameraDurationSeconds = 2f;

    private Coroutine puzzleCameraRoutine;
    private int cachedPuzzleCameraPriority;
    private bool hasInteracted;

    public void EnableInteraction()
    {
        enabled = true;
    }

    public void DisableInteraction()
    {
        enabled = false;
    }

    public void SetInteractionEnabled(bool isEnabled)
    {
        enabled = isEnabled;
    }

    protected override void OnTriggerEnter(Collider other)
    {
        base.OnTriggerEnter(other);

        if (!other.transform.root.CompareTag("Player"))
            return;

        if (needsItem && !canUnlock && InteractionUI.Instance != null && InteractionUI.Instance._interactText != null)
        {
            InteractionUI.Instance._interactText.text = string.IsNullOrWhiteSpace(lockedInteractionPrompt)
                ? "LOCKED"
                : lockedInteractionPrompt;
        }
    }

    protected override void ExecuteInteraction()
    {
        if (onlyInteractableOnce && hasInteracted)
            return;

        if (usePuzzleCameraOnInteraction)
            BeginTemporaryPuzzleCamera();

        bool executedInteraction = false;

        foreach (DoorHandler doorHandler in doorHandlers)
        {
            if (doorHandler != null)
            {
                if (doorHandler.doorLockState == DoorHandler.DoorLockState.Locked)
                    doorHandler.UnlockDoor();
                

                doorHandler.Interact();
                executedInteraction = true;
            }
        }

        if (onlyInteractableOnce && executedInteraction)
        {
            hasInteracted = true;
            enabled = false;
        }
    }

    private void BeginTemporaryPuzzleCamera()
    {
        if (puzzleCinemachineCamera == null)
        {
            Debug.LogWarning("[DoorInteractions] 'Use puzzle camera on interaction' is enabled but no puzzle camera is assigned.");
            return;
        }

        if (puzzleCameraRoutine != null)
            StopCoroutine(puzzleCameraRoutine);

        puzzleCameraRoutine = StartCoroutine(PuzzleCameraRoutine());
    }

    private IEnumerator PuzzleCameraRoutine()
    {
        cachedPuzzleCameraPriority = puzzleCinemachineCamera.Priority;
        puzzleCinemachineCamera.Priority = 21;

        float duration = Mathf.Max(0f, puzzleCameraDurationSeconds);
        if (duration > 0f)
            yield return new WaitForSeconds(duration);

        puzzleCinemachineCamera.Priority = cachedPuzzleCameraPriority;

        puzzleCameraRoutine = null;
    }
}
