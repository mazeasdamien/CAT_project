using UnityEngine;

public class CarrotController: MonoBehaviour
{
    public Transform realRobotPosition;
    public Transform userControlTarget;

    public float leadDistance = 0.05f;

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
    }
}