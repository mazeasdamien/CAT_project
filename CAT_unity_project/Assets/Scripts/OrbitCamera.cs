using UnityEngine;
using UnityEngine.EventSystems;

public class OrbitCamera : MonoBehaviour
{
    [Tooltip("The target object to orbit around.")]
    public Transform target;

    [Tooltip("Initial distance from the target.")]
    public float distance = 5.0f;

    [Tooltip("Speed of horizontal orbit rotation (mouse movement).")]
    public float xSpeed = 120.0f;

    [Tooltip("Speed of vertical orbit rotation (mouse movement).")]
    public float ySpeed = 120.0f;

    [Tooltip("Minimum vertical angle limit.")]
    public float yMinLimit = 0f;

    [Tooltip("Maximum vertical angle limit.")]
    public float yMaxLimit = 80f;

    [Tooltip("Minimum zoom distance.")]
    public float distanceMin = .5f;

    [Tooltip("Maximum zoom distance.")]
    public float distanceMax = 15f;

    [Tooltip("Speed of zooming with scroll wheel.")]
    public float zoomRate = 1.0f;

    [Tooltip("Speed of panning.")]
    public float panSpeed = 0.3f;

    [Tooltip("Which mouse button to use for panning.")]
    public PanButton panButton = PanButton.RightClick;

    public enum PanButton
    {
        RightClick = 1,
        MiddleClick = 2
    }

    private Vector3 targetOffset = Vector3.zero;

    [Header("Auto Rotation Settings")]
    [Tooltip("Enable or disable auto-rotation when idle.")]
    public bool enableAutoRotate = true;

    [Tooltip("Horizontal auto-rotation speed.")]
    public float autoRotateSpeedX = 5.0f;

    [Tooltip("Vertical auto-rotation speed.")]
    public float autoRotateSpeedY = 1.0f;

    [Tooltip("Time in seconds before auto-rotation starts after last input.")]
    public float idleTime = 5.0f;

    [Tooltip("Speed at which auto-rotation accelerates to full speed.")]
    public float autoRotateLerpSpeed = 0.5f;

    private float lastInputTime;
    private float currentAutoRotateX;
    private float currentAutoRotateY;

    float x = 0.0f;
    float y = 0.0f;

    private bool canInteract = true;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;

        // Make the rigid body not change rotation
        // Make the rigid body not change rotation
        if (TryGetComponent(out Rigidbody rb))
        {
            rb.freezeRotation = true;
        }

        lastInputTime = Time.time;
    }

    void LateUpdate()
    {
        if (target)
        {
            bool hasInput = false;

            int panBtnIndex = (int)panButton;

            // Check if mouse is over UI when clicking
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(panBtnIndex))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                {
                    canInteract = false;
                }
                else
                {
                    canInteract = true;
                }
            }

            // If no buttons are held, reset interaction state
            if (!Input.GetMouseButton(0) && !Input.GetMouseButton(panBtnIndex))
            {
                canInteract = true;
            }

            if (canInteract)
            {
                // Check for left mouse button click and drag (Orbit)
                if (Input.GetMouseButton(0))
                {
                    x += Input.GetAxis("Mouse X") * xSpeed * 0.02f;
                    y -= Input.GetAxis("Mouse Y") * ySpeed * 0.02f;
                    hasInput = true;
                }

                // Check for pan button click and drag (Pan)
                if (Input.GetMouseButton(panBtnIndex))
                {
                    targetOffset -= transform.right * (Input.GetAxis("Mouse X") * panSpeed);
                    targetOffset -= transform.up * (Input.GetAxis("Mouse Y") * panSpeed);
                    hasInput = true;
                }
            }

            // Optional: Scroll to zoom
            // Only zoom if not over UI
            if (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject())
            {
                float scroll = Input.GetAxis("Mouse ScrollWheel");
                if (Mathf.Abs(scroll) > 0.001f)
                {
                    distance = Mathf.Clamp(distance - scroll * zoomRate, distanceMin, distanceMax);
                    hasInput = true;
                }
            }

            if (hasInput)
            {
                lastInputTime = Time.time;
                currentAutoRotateX = 0f;
                currentAutoRotateY = 0f;
            }
            else if (enableAutoRotate && Time.time - lastInputTime > idleTime)
            {
                // Smoothly interpolate current rotation speed towards target speed
                currentAutoRotateX = Mathf.Lerp(currentAutoRotateX, autoRotateSpeedX, Time.deltaTime * autoRotateLerpSpeed);
                currentAutoRotateY = Mathf.Lerp(currentAutoRotateY, autoRotateSpeedY, Time.deltaTime * autoRotateLerpSpeed);

                // Apply rotation
                x += currentAutoRotateX * Time.deltaTime;
                y += currentAutoRotateY * Time.deltaTime;
            }

            y = ClampAngle(y, yMinLimit, yMaxLimit);

            Quaternion rotation = Quaternion.Euler(y, x, 0);

            if (Physics.Linecast(target.position, transform.position, out RaycastHit hit))
            {
                distance -= hit.distance;
            }


            Vector3 negDistance = new(0.0f, 0.0f, -distance);
            Vector3 position = rotation * negDistance + target.position + targetOffset;

            transform.SetPositionAndRotation(position, rotation);
        }
    }

    public static float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360F)
            angle += 360F;
        if (angle > 360F)
            angle -= 360F;
        return Mathf.Clamp(angle, min, max);
    }
}
