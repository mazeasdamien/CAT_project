using UnityEngine;

public class TransformMatcher : MonoBehaviour
{
    [Tooltip("The target GameObject (GameObject 1) to follow.")]
    public Transform target;

    [Tooltip("If true, updates in LateUpdate to ensure the target has finished moving for the frame. Prevents jitter.")]
    public bool useLateUpdate = true;

    void Update()
    {
        // If we are not using LateUpdate, update here
        if (!useLateUpdate && target != null)
        {
            MatchTransform();
        }
    }

    void LateUpdate()
    {
        // LateUpdate is generally better for following objects to avoid lag/jitter
        if (useLateUpdate && target != null)
        {
            MatchTransform();
        }
    }

    void MatchTransform()
    {
        // Using .position and .rotation sets the WORLD space coordinates.
        // This works regardless of what parents the objects have.
        transform.position = target.position;
        transform.rotation = target.rotation;
    }
}