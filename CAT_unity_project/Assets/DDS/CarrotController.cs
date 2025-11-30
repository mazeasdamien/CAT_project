using UnityEngine;
using UnityEngine;

public class CarrotController: MonoBehaviour
{
    public Transform realRobotPosition;
    public Transform userControlTarget;

    [Tooltip("Distance in meters to lead the robot. Increased to 0.2m to maintain momentum.")]
    public float leadDistance = 0.2f; // Changed from 0.05f

    [Header("Singularity Avoidance")]
    [Tooltip("Radius of the singularity cylinder around the Z-axis (Robot Base).")]
    public float singularityRadius = 0.1f; // 10cm

    private void Update()
    {
        if (realRobotPosition == null || userControlTarget == null) return;

        CalculateCarrotPosition();
        CalculateCarrotRotation();
    }

    private void CalculateCarrotPosition()
    {
        Vector3 currentPos = realRobotPosition.position;
        Vector3 targetPos = userControlTarget.position;
        Vector3 direction = targetPos - currentPos;
        float totalDistance = direction.magnitude;

        if (totalDistance < 0.001f)
        {
            transform.position = targetPos;
        }
        else
        {
            float clampDist = Mathf.Min(totalDistance, leadDistance);
            transform.position = currentPos + (direction.normalized * clampDist);
        }
    }

    private void CalculateCarrotRotation()
    {
        Vector3 currentPos = realRobotPosition.position;
        Vector3 targetPos = userControlTarget.position;
        float totalDistance = Vector3.Distance(currentPos, targetPos);

        float rotationFactor = (totalDistance > 0) ? Mathf.Clamp01(leadDistance / totalDistance) : 1f;
        
        transform.rotation = Quaternion.Slerp(realRobotPosition.rotation, userControlTarget.rotation, rotationFactor);
    }

    private void OnDrawGizmos()
    {
        if (realRobotPosition != null && userControlTarget != null)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawLine(realRobotPosition.position, userControlTarget.position);

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.position, 0.02f);
            Gizmos.DrawLine(realRobotPosition.position, transform.position);
        }

        // Visualize Singularity Zone (Cylinder around 0,0,0 assuming Robot Base is World Origin)
        Gizmos.color = new Color(1, 0, 0, 0.3f); // Semi-transparent Red
        
        // Draw Cylinder circles
        int segments = 32;
        float angleStep = 360f / segments;
        float height = 2.0f; // Visual height
        
        Vector3 prevPtTop = new Vector3(Mathf.Cos(0) * singularityRadius, height, Mathf.Sin(0) * singularityRadius);
        Vector3 prevPtBot = new Vector3(Mathf.Cos(0) * singularityRadius, -0.5f, Mathf.Sin(0) * singularityRadius);

        for (int i = 1; i <= segments + 1; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            Vector3 nextPtTop = new Vector3(Mathf.Cos(angle) * singularityRadius, height, Mathf.Sin(angle) * singularityRadius);
            Vector3 nextPtBot = new Vector3(Mathf.Cos(angle) * singularityRadius, -0.5f, Mathf.Sin(angle) * singularityRadius);

            Gizmos.DrawLine(prevPtTop, nextPtTop);
            Gizmos.DrawLine(prevPtBot, nextPtBot);
            if (i % 4 == 0) Gizmos.DrawLine(prevPtTop, prevPtBot); // Vertical lines occasionally

            prevPtTop = nextPtTop;
            prevPtBot = nextPtBot;
        }

        // Check if Carrot is in Singularity
        // Assuming Robot Base is at (0,0,0)
        float distSq = (transform.position.x * transform.position.x) + (transform.position.z * transform.position.z);
        if (distSq < singularityRadius * singularityRadius)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, new Vector3(0, transform.position.y, 0));
            Gizmos.DrawSphere(transform.position, 0.05f);
        }
    }
}