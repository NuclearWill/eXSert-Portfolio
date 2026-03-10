using UnityEngine;
using System.Collections;
using Unity.VisualScripting;

public class BasicDoor : DoorHandler
{
    public enum BasicDoorType { OpenOut, OpenIn }

    [Tooltip("Type of door movement")]
    public BasicDoorType doorType;

    [Header("Hinge Settings (for OpenOut/OpenIn)")]
    [Tooltip("Optional hinge pivot. If null, a pivot GameObject will be created at the door origin.")]
    [SerializeField] private Transform hingePivot;
    // Hinge variables that are used for OpenIn and OpenOut door types
    private Quaternion hingeStartRot;
    private Quaternion hingeTargetRot;
    private Quaternion hingeOriginalRot; // Store the original hinge rotation before any animation
    private Coroutine hingeAnimCoroutine = null;

    protected override void Awake()
    {
        base.Awake();

        // If a hinge pivot is provided, store its original rotation
        if (hingePivot != null)
        {
            hingeOriginalRot = hingePivot.rotation;
        }
    }

    protected override void OpenDoorBasedOnType()
    {
        switch (doorType)
        {
            case BasicDoorType.OpenOut:
                OpenOut();
                break;
            case BasicDoorType.OpenIn:
                OpenIn();
                break;
            default:
                Debug.LogWarning("Unsupported door type for BasicDoor: " + doorType);
                break;
        }
    }

    protected override void CloseDoorBasedOnType()
    {
        StartHingeAnimation(hingePivot.rotation, hingeOriginalRot, 1f / openSpeed);
    }

    private void OpenOut()
    {
        // Use hinge pivot to rotate outwards so the door stays locked in its socket
        EnsurePivot();
        hingeStartRot = hingePivot.rotation;
        hingeTargetRot = hingeOriginalRot * Quaternion.Euler(0f, -90f, 0f);
        StartHingeAnimation(hingeStartRot, hingeTargetRot, 1f / openSpeed);
    }

    private void OpenIn()
    {
        // Use hinge pivot to rotate inwards so the door stays locked in its socket
        EnsurePivot();
        hingeStartRot = hingePivot.rotation;
        hingeTargetRot = hingeOriginalRot * Quaternion.Euler(0f, 90f, 0f);
        StartHingeAnimation(hingeStartRot, hingeTargetRot, 1f / openSpeed); 
    }

     private void EnsurePivot()
    {
        // If the hinge pivot doesn't exist, create it at the door's origin
        if (hingePivot == null)
        {
            GameObject go = new GameObject(this.gameObject.name + "PivotPoint");
            go.transform.position = transform.position;
            go.transform.rotation = transform.rotation;
            // parent pivot to the door's original parent to keep hierarchy
            go.transform.SetParent(this.transform.parent, true);
            hingePivot = go.transform;
            // Store the original hinge rotation for closing animation (first time only)
            hingeOriginalRot = hingePivot.rotation;
            // store original parent and reparent the door to hinge while preserving world transform
            originalParent = this.transform.parent;
            this.transform.SetParent(hingePivot, true);
        }
        else
        {
            //if there is a pivot point already, just make sure the door is parented to it
            if (this.transform.parent != hingePivot)
            {
                originalParent = this.transform.parent;
                this.transform.SetParent(hingePivot, true);
            }
        }
    }

    // Start hinge animation coroutine
    private void StartHingeAnimation(Quaternion from, Quaternion to, float duration)
    {
        if (hingeAnimCoroutine != null)
            StopCoroutine(hingeAnimCoroutine);
        hingeAnimCoroutine = StartCoroutine(AnimateHinge(from, to, duration));
    }

    // Coroutine to animate the hinge rotation smoothly over time. from == starting rotation, to == target rotation
    private IEnumerator AnimateHinge(Quaternion from, Quaternion to, float duration)
    {
        if (hingePivot == null)
            yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Animate the hinge rotation smoothly over time
            float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
            hingePivot.rotation = Quaternion.Slerp(from, to, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        hingePivot.rotation = to;
        hingeAnimCoroutine = null;
    }
}
