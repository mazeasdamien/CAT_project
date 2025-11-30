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
    [Tooltip("Movement speed in units per second.")]
    public float speed = 0.5f;

    [Tooltip("Distance threshold to consider the target reached.")]
    public float reachThreshold = 0.01f;

    [Header("Area Limits (Local Space)")]
    // Default values provided by user
    public Vector3 limit1 = new Vector3(-0.745999992f, -0.59799999f, 0.600000024f);
    public Vector3 limit2 = new Vector3(-0.201000005f, 0.426999986f, 0.103f);

    [Header("Rotation Limits (Euler Angles)")]
    public Vector3 rotLimit1 = new Vector3(1.23020911f, 181.327133f, 269.087036f);
    public Vector3 rotLimit2 = new Vector3(21.0933533f, 245.063507f, 242.05864f);

    [Tooltip("Rotation speed in degrees per second.")]
    public float rotationSpeed = 30.0f;

    private Vector3 _targetPosition;
    private Quaternion _targetRotation;

    private Vector3 _minBounds;
    private Vector3 _maxBounds;

    private Vector3 _minRotBounds;
    private Vector3 _maxRotBounds;

    private void Start()
    {
        CalculateBounds();
        SetNewRandomTarget();
    }

    private void Update()
    {
        if (!isMoving) return;

        // Move smoothly towards the target position
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, _targetPosition, speed * Time.deltaTime);

        // Rotate smoothly towards the target rotation
        transform.localRotation = Quaternion.RotateTowards(transform.localRotation, _targetRotation, rotationSpeed * Time.deltaTime);

        // Check if we have reached the target (both position and rotation)
        float dist = Vector3.Distance(transform.localPosition, _targetPosition);
        float angle = Quaternion.Angle(transform.localRotation, _targetRotation);

        if (dist < reachThreshold && angle < 1.0f)
        {
            SetNewRandomTarget();
        }
    }

    /// <summary>
    /// Calculates the min and max bounds from the two limit points for both position and rotation.
    /// </summary>
    private void CalculateBounds()
    {
        _minBounds = Vector3.Min(limit1, limit2);
        _maxBounds = Vector3.Max(limit1, limit2);

        _minRotBounds = Vector3.Min(rotLimit1, rotLimit2);
        _maxRotBounds = Vector3.Max(rotLimit1, rotLimit2);
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

        // Rotation
        float rx = Random.Range(_minRotBounds.x, _maxRotBounds.x);
        float ry = Random.Range(_minRotBounds.y, _maxRotBounds.y);
        float rz = Random.Range(_minRotBounds.z, _maxRotBounds.z);
        _targetRotation = Quaternion.Euler(rx, ry, rz);
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
