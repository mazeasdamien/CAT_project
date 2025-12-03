using UnityEngine;

public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 5.0f;
    public float xSpeed = 120.0f;
    public float ySpeed = 120.0f;

    public float yMinLimit = -20f;
    public float yMaxLimit = 80f;

    public float distanceMin = .5f;
    public float distanceMax = 15f;

    public float zoomRate = 1.0f;

    public float panSpeed = 0.3f;
    private Vector3 targetOffset = Vector3.zero;

    float x = 0.0f;
    float y = 0.0f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;

        // Make the rigid body not change rotation
        if (GetComponent<Rigidbody>())
        {
            GetComponent<Rigidbody>().freezeRotation = true;
        }
    }

    void LateUpdate()
    {
        if (target)
        {
            // Check for left mouse button click and drag (Orbit)
            if (Input.GetMouseButton(0))
            {
                x += Input.GetAxis("Mouse X") * xSpeed * 0.02f;
                y -= Input.GetAxis("Mouse Y") * ySpeed * 0.02f;
            }

            // Check for right mouse button click and drag (Pan)
            if (Input.GetMouseButton(1))
            {
                targetOffset -= transform.right * Input.GetAxis("Mouse X") * panSpeed;
                targetOffset -= transform.up * Input.GetAxis("Mouse Y") * panSpeed;
            }

            y = ClampAngle(y, yMinLimit, yMaxLimit);

            Quaternion rotation = Quaternion.Euler(y, x, 0);

            // Optional: Scroll to zoom
            distance = Mathf.Clamp(distance - Input.GetAxis("Mouse ScrollWheel") * zoomRate, distanceMin, distanceMax);

            /*
             * If you want to check for collisions so the camera doesn't clip through walls:
             * RaycastHit hit;
             * if (Physics.Linecast(target.position, transform.position, out hit)) 
             * {
             *     distance -= hit.distance;
             * }
             */

            Vector3 negDistance = new Vector3(0.0f, 0.0f, -distance);
            Vector3 position = rotation * negDistance + target.position + targetOffset;

            transform.rotation = rotation;
            transform.position = position;
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
