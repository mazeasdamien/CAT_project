using UnityEngine;

/// <summary>
/// Moves the GameObject smoothly and continuously to random positions within a defined local area.
/// </summary>
public class RandomLocalMover : MonoBehaviour
{
    [Header("Control")]
    [Tooltip("Enable or disable the random movement.")]
    public bool isMoving = true;

    [Header("Movement Settings")]
    [Tooltip("Lerp speed for smooth movement.")]
    public float speed = 2.0f;

    [Tooltip("Distance threshold to consider the target reached.")]
    public float reachThreshold = 0.05f;

    [Header("Area Limits (Local Space)")]
    // Default values provided by user
    public Vector3 limit1 = new Vector3(-0.745999992f, -0.59799999f, 0.600000024f);
    public Vector3 limit2 = new Vector3(-0.201000005f, 0.426999986f, 0.103f);

    [Header("Rotation Settings")]
    [Tooltip("Maximum angle deviation from straight down (in degrees).")]
    public float tiltTolerance = 20.0f;

    private Vector3 _targetPosition;
    private Quaternion _targetRotation;

    private Vector3 _minBounds;
    private Vector3 _maxBounds;

    private void Start()
    {
        CalculateBounds();
        SetNewRandomTarget();
    }

    private void Update()
    {
        if (!isMoving) return;

        // Move smoothly towards the target position using Lerp for natural feel
        transform.localPosition = Vector3.Lerp(transform.localPosition, _targetPosition, speed * Time.deltaTime);

        // Rotate smoothly towards the target rotation using Slerp
        transform.localRotation = Quaternion.Slerp(transform.localRotation, _targetRotation, speed * Time.deltaTime);

        // Check if we have reached the target (mostly position, rotation follows)
        float dist = Vector3.Distance(transform.localPosition, _targetPosition);
        float angle = Quaternion.Angle(transform.localRotation, _targetRotation);

        if (dist < reachThreshold && angle < 5.0f)
        {
            SetNewRandomTarget();
        }
    }

    /// <summary>
    /// Calculates the min and max bounds from the two limit points for position.
    /// </summary>
    private void CalculateBounds()
    {
        _minBounds = Vector3.Min(limit1, limit2);
        _maxBounds = Vector3.Max(limit1, limit2);
    }

    /// <summary>
    /// Picks a new random position and rotation within the calculated bounds.
    /// </summary>
    private void SetNewRandomTarget()
    {
        // Position
        float x = Random.Range(_minBounds.x, _maxBounds.x);
        float y = Random.Range(_minBounds.y, _maxBounds.y);
        float z = Random.Range(_minBounds.z, _maxBounds.z);
        _targetPosition = new Vector3(x, y, z);

        // Rotation: Z axis facing floor (World Down) with tolerance
        Vector3 worldDown = Vector3.down;

        // Generate a random direction within the tolerance cone
        // Rotate 'down' towards a random direction by a random angle within tolerance
        Vector3 targetZ = Vector3.RotateTowards(worldDown, Random.onUnitSphere, Random.Range(0, tiltTolerance) * Mathf.Deg2Rad, 0.0f);

        // Create a rotation looking at targetZ with a random Up vector to allow natural variation
        Quaternion worldRot = Quaternion.LookRotation(targetZ, Random.onUnitSphere);

        // Convert to Local Rotation
        if (transform.parent != null)
        {
            _targetRotation = Quaternion.Inverse(transform.parent.rotation) * worldRot;
        }
        else
        {
            _targetRotation = worldRot;
        }
    }

    /// <summary>
    /// Visualizes the movement area and current target in the Editor.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        CalculateBounds();

        // Draw the bounding box
        Gizmos.color = Color.yellow;
        Vector3 center = (_minBounds + _maxBounds) / 2;
        Vector3 size = _maxBounds - _minBounds;

        // Transform the local bounds to world space for drawing
        if (transform.parent != null)
        {
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.parent.localToWorldMatrix;
            Gizmos.DrawWireCube(center, size);

            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_targetPosition, 0.05f);

            // Visualize Rotation Target (small ray)
            Gizmos.color = Color.blue;
            Vector3 targetWorldPos = transform.parent.TransformPoint(_targetPosition);
            Quaternion targetWorldRot = transform.parent.rotation * _targetRotation;
            Gizmos.matrix = Matrix4x4.identity; // Reset to world for drawing the ray
            Gizmos.DrawRay(targetWorldPos, targetWorldRot * Vector3.forward * 0.2f);

            Gizmos.matrix = oldMatrix;
        }
        else
        {
            // If no parent, local is world
            Gizmos.DrawWireCube(center, size);
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(_targetPosition, 0.05f);

            Gizmos.color = Color.blue;
            Gizmos.DrawRay(_targetPosition, _targetRotation * Vector3.forward * 0.2f);
        }
    }
}
