using UnityEngine;
using System.Collections.Generic;
using System.Collections;

//Once the pieces are in the list, you can set which axes they move on and their min/max positions
[System.Serializable]
public class DoorPartMovement
{
    [Tooltip("GameObject to move")]
    public GameObject partObject;
    
    [Tooltip("Enable movement on X axis")]
    public bool moveX = false;
    [Tooltip("Enable movement on Y axis")]
    public bool moveY = false;
    [Tooltip("Enable movement on Z axis")]
    public bool moveZ = false;
    
    public float distToOpenParts = 2.0f;
    [HideInInspector] public Vector3 closedLocalPosition;
}
public class SlidingDoor : DoorHandler
{
    [Space(10), Tooltip("Only used for OpenUnconvential door type. List of door parts to move and their movement settings.")]
    public List<DoorPartMovement> doorParts = new List<DoorPartMovement>();

    protected override void OpenDoorBasedOnType()
    {
        OpenUnconvential();
    }

    protected override void CloseDoorBasedOnType()
    {
        CloseUnconvential();
    }

     // These two functions handle the OpenUp door type
    private void OpenUnconvential()
    {
        // Store closed local positions at the moment of opening
        foreach (var partMove in doorParts)
        {
            if (partMove.partObject != null)
                partMove.closedLocalPosition = partMove.partObject.transform.localPosition;
        }
        StopAllCoroutines();
        StartCoroutine(OpenUnconventialCoroutine());
    }

    private void CloseUnconvential()
    {
        StopAllCoroutines();
        StartCoroutine(CloseUnconventialCoroutine());
    }

    // Ensure the hinge pivot exists and is parent of the door
   

    // Coroutines for opening and closing the door upwards
    private IEnumerator OpenUnconventialCoroutine()
    {
        if (doorParts == null || doorParts.Count == 0)
        {
            yield break;
        }

        // Prepare target local positions for each part
        List<GameObject> movingParts = new List<GameObject>();
        List<Vector3> targetLocalPositions = new List<Vector3>();

        foreach (var partMove in doorParts)
        {
            if (partMove.partObject == null) continue;
            Vector3 offset = new Vector3(
                partMove.moveX ? partMove.distToOpenParts : 0f,
                partMove.moveY ? partMove.distToOpenParts : 0f,
                partMove.moveZ ? partMove.distToOpenParts : 0f
            );
            movingParts.Add(partMove.partObject);
            // Use closedLocalPosition as base
            targetLocalPositions.Add(partMove.closedLocalPosition + offset);
        }

        if (movingParts.Count == 0)
        {
            yield break;
        }

        bool allAtTarget = false;
        while (!allAtTarget)
        {
            allAtTarget = true;
            for (int i = 0; i < movingParts.Count; i++)
            {
                var part = movingParts[i];
                var target = targetLocalPositions[i];
                if (part == null) continue;
                if (Vector3.Distance(part.transform.localPosition, target) > 0.01f)
                {
                    float t = Mathf.Clamp01(openSpeed * Time.deltaTime);
                    part.transform.localPosition = Vector3.Lerp(part.transform.localPosition, target, t);
                    allAtTarget = false;
                }
            }
            yield return null;
        }
        isOpened = true;
        yield return null;
    }

    private IEnumerator CloseUnconventialCoroutine()
    {
        if (doorParts == null || doorParts.Count == 0)
        {
            yield break;
        }

        // Prepare closed local positions for each part (return to closedLocalPosition)
        List<GameObject> movingParts = new List<GameObject>();
        List<Vector3> closedLocalPositions = new List<Vector3>();

        foreach (var partMove in doorParts)
        {
            if (partMove.partObject == null) continue;
            movingParts.Add(partMove.partObject);
            closedLocalPositions.Add(partMove.closedLocalPosition);
        }

        if (movingParts.Count == 0)
        {
            yield break;
        }

        bool allAtClosed = false;
        while (!allAtClosed)
        {
            allAtClosed = true;
            for (int i = 0; i < movingParts.Count; i++)
            {
                var part = movingParts[i];
                var target = closedLocalPositions[i];
                if (part == null) continue;
                if (Vector3.Distance(part.transform.localPosition, target) > 0.01f)
                {
                    float t = Mathf.Clamp01(openSpeed * Time.deltaTime);
                    part.transform.localPosition = Vector3.Lerp(part.transform.localPosition, target, t);
                    allAtClosed = false;
                }
            }
            yield return null;
        }
        isOpened = false;
        yield return null;
    }
}
