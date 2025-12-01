using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class FanucJacobianIK : MonoBehaviour
{
    [Header("IK Targets")]
    [Tooltip("The target Transform that the robot's end effector should attempt to reach.")]
    public Transform targetTransform;

    [Tooltip("The Transform representing the robot's end effector (TCP). This should be a child of the last joint.")]
    public Transform endEffector;

    [Header("Robot Joints (J1 - J6)")]
    [Tooltip("List of ArticulationBody components representing the robot's joints (J1 to J6).")]
    public List<ArticulationBody> joints;

    [Header("UI")]
    [Tooltip("Assign a TextMeshProUGUI to see the joint angles in real-time.")]
    public TextMeshProUGUI jointSolutionDisplay;

    [Header("Solver Settings")]
    [Tooltip("If true, the IK solver will actively update joint angles to reach the target.")]
    public bool isSolving = true;

    [Tooltip("How fast the robot converges. Higher = Snappier, Lower = Smoother.")]
    [Range(0.01f, 5.0f)] public float learningRate = 0.8f;

    [Tooltip("Damping prevents the robot from jittering near singularities.")]
    [Range(0.001f, 1.0f)] public float damping = 0.05f;

    [Tooltip("Stop solving if error is smaller than this.")]
    public float stopThreshold = 0.001f;

    [Tooltip("Offset to apply to the end effector rotation to match the target frame.")]
    public Vector3 rotationOffset;

    [Header("Debug")]
    [Tooltip("If true, draws debug lines in the Scene view showing the target and end effector frames.")]
    public bool drawGizmos = true;

    [Tooltip("If true, logs a warning to the console when the robot approaches a wrist singularity (J5 near 0).")]
    public bool showSingularityWarning = true;

    // Internal Math Buffers
    private float[] _theta; // Joint angles
    private float[] _error; // 6x1 Error Vector (3 Pos + 3 Rot)
    private float[,] _jacobian; // 6xN Matrix

    private void Start()
    {
        int n = joints.Count;
        _theta = new float[n];
        _error = new float[6];
        _jacobian = new float[6, n];
    }

    private void Update()
    {
        if (isSolving && targetTransform != null && endEffector != null)
        {
            SolveJacobian();
            CheckSingularity();
        }
    }

    private void CheckSingularity()
    {
        if (!showSingularityWarning || joints.Count < 5) return;

        // Fanuc Wrist Singularity is typically when J5 is near 0.
        // This aligns J4 and J6, causing the Jacobian to lose rank.
        float j5Angle = joints[4].jointPosition[0] * Mathf.Rad2Deg;
        if (Mathf.Abs(j5Angle) < 2.0f)
        {
            // Warning removed as requested
        }
    }

    private void SolveJacobian()
    {
        int n = joints.Count; // Number of joints

        // 1. Calculate Error Vector (6x1)
        // Position Error
        Vector3 posDiff = targetTransform.position - endEffector.position;
        _error[0] = posDiff.x;
        _error[1] = posDiff.y;
        _error[2] = posDiff.z;

        // Rotation Error (Axis-Angle approach)
        // We find the quaternion that rotates 'current' to 'target'
        // Apply offset to end effector rotation
        Quaternion currentRot = endEffector.rotation * Quaternion.Euler(rotationOffset);
        Quaternion rotDiff = targetTransform.rotation * Quaternion.Inverse(currentRot);

        rotDiff.ToAngleAxis(out float angle, out Vector3 axis);

        // Normalize angle to -180 to 180
        if (angle > 180f) angle -= 360f;

        // Convert degrees to radians for the math, then scale axis
        // Note: We dampen rotation slightly (0.0174 is deg2rad)
        Vector3 rotErrorVec = axis.normalized * (angle * Mathf.Deg2Rad);

        _error[3] = rotErrorVec.x;
        _error[4] = rotErrorVec.y;
        _error[5] = rotErrorVec.z;

        // Optimization: Stop if close enough
        if (posDiff.magnitude < stopThreshold && Mathf.Abs(angle) < 0.5f)
        {
            UpdateUI();
            return;
        }

        // 2. Build Jacobian Matrix (6 x N)
        // The Jacobian describes how each joint's movement affects the end effector
        for (int i = 0; i < n; i++)
        {
            ArticulationBody joint = joints[i];

            // Calculate rotation axis in World Space
            // ArticulationBodies usually rotate around X relative to their anchor
            Quaternion jointRot = joint.transform.rotation * joint.anchorRotation;
            Vector3 jAxis = jointRot * Vector3.right; // Axis of rotation
            Vector3 jPos = joint.transform.position;  // Pivot point

            // Linear Velocity Component (Cross Product of Axis and Vector to Effector)
            Vector3 posEffect = Vector3.Cross(jAxis, endEffector.position - jPos);

            // Fill Column i
            _jacobian[0, i] = posEffect.x;
            _jacobian[1, i] = posEffect.y;
            _jacobian[2, i] = posEffect.z;

            // Angular Velocity Component (Just the Axis)
            _jacobian[3, i] = jAxis.x;
            _jacobian[4, i] = jAxis.y;
            _jacobian[5, i] = jAxis.z;
        }

        // 3. Solve for dTheta (Change in Angles) using Damped Least Squares (DLS)
        // Formula: dTheta = J_Transpose * (J * J_Transpose + Lambda^2 * Identity)^-1 * Error

        // This looks scary, but it's the standard way to solve IK without "Exploding"
        float[] dTheta = CalculateDLS(n);

        // 4. Apply changes to joints
        for (int i = 0; i < n; i++)
        {
            float deltaDeg = dTheta[i] * Mathf.Rad2Deg * learningRate;
            float currentDeg = joints[i].jointPosition[0] * Mathf.Rad2Deg;
            float targetDeg = currentDeg + deltaDeg;

            // Clamp to Limits
            if (joints[i].xDrive.lowerLimit < joints[i].xDrive.upperLimit)
            {
                targetDeg = Mathf.Clamp(targetDeg, joints[i].xDrive.lowerLimit, joints[i].xDrive.upperLimit);
            }

            var drive = joints[i].xDrive;
            drive.target = targetDeg;
            joints[i].xDrive = drive;
        }

        UpdateUI();
    }

    private void UpdateUI()
    {
        if (jointSolutionDisplay != null && joints != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < joints.Count; i++)
            {
                float val = joints[i].jointPosition[0] * Mathf.Rad2Deg;

                // Fanuc J2-J3 Coupling: J3_Unity = J3_Fanuc + J2_Fanuc
                // Therefore to display Fanuc J3, we must subtract J2:
                if (i == 2 && joints.Count > 1)
                {
                    float j2Val = joints[1].jointPosition[0] * Mathf.Rad2Deg;
                    val -= j2Val;
                }

                sb.AppendLine($"J{i + 1}: {val:F2}");
            }
            jointSolutionDisplay.text = sb.ToString();
        }
    }

    private void OnDrawGizmos()
    {
        if (!drawGizmos) return;

        if (endEffector != null)
        {
            // Draw End Effector Frame (with offset)
            Quaternion effectiveRot = endEffector.rotation * Quaternion.Euler(rotationOffset);
            Vector3 pos = endEffector.position;

            Gizmos.color = Color.red;
            Gizmos.DrawRay(pos, effectiveRot * Vector3.right * 0.2f);
            Gizmos.color = Color.green;
            Gizmos.DrawRay(pos, effectiveRot * Vector3.up * 0.2f);
            Gizmos.color = Color.blue;
            Gizmos.DrawRay(pos, effectiveRot * Vector3.forward * 0.2f);
        }

        if (targetTransform != null)
        {
            // Draw Target Frame
            Vector3 pos = targetTransform.position;
            Quaternion rot = targetTransform.rotation;

            Gizmos.color = Color.red; // X
            Gizmos.DrawRay(pos, rot * Vector3.right * 0.2f);
            Gizmos.color = Color.green; // Y
            Gizmos.DrawRay(pos, rot * Vector3.up * 0.2f);
            Gizmos.color = Color.blue; // Z
            Gizmos.DrawRay(pos, rot * Vector3.forward * 0.2f);
        }
    }

    // --- LINEAR ALGEBRA HELPERS ---

    // Calculates: J^T * (J * J^T + lambda^2 * I)^-1 * e
    private float[] CalculateDLS(int cols)
    {
        int rows = 6; // 3 Pos + 3 Rot

        // 1. Compute JJT (J * J_Transpose) -> Result is 6x6
        float[,] JJT = new float[rows, rows];
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < rows; c++)
            {
                float sum = 0f;
                for (int k = 0; k < cols; k++)
                {
                    sum += _jacobian[r, k] * _jacobian[c, k];
                }
                JJT[r, c] = sum;
            }
        }

        // 2. Add Damping (lambda^2) to the diagonal
        float dampingSq = damping * damping;
        for (int i = 0; i < rows; i++)
        {
            JJT[i, i] += dampingSq;
        }

        // 3. Solve (JJT) * x = Error for x.
        // Since JJT is 6x6, we can use Gaussian Elimination.
        float[] x_vec = SolveLinearSystem(JJT, _error, rows);

        // 4. Compute dTheta = J_Transpose * x_vec
        float[] dTheta = new float[cols];
        for (int i = 0; i < cols; i++)
        {
            float sum = 0f;
            for (int j = 0; j < rows; j++)
            {
                // Note indices swapped for Transpose
                sum += _jacobian[j, i] * x_vec[j];
            }
            dTheta[i] = sum;
        }

        return dTheta;
    }

    // Simple Gaussian Elimination to solve A * x = b for x
    // A must be N*N, b must be size N
    private float[] SolveLinearSystem(float[,] A, float[] b, int n)
    {
        // Clone to avoid modifying originals during pivot
        float[,] M = (float[,])A.Clone();
        float[] x = (float[])b.Clone();

        // Forward Elimination
        for (int k = 0; k < n - 1; k++)
        {
            for (int i = k + 1; i < n; i++)
            {
                float factor = M[i, k] / M[k, k];
                for (int j = k; j < n; j++)
                {
                    M[i, j] -= factor * M[k, j];
                }
                x[i] -= factor * x[k];
            }
        }

        // Back Substitution
        float[] result = new float[n];
        for (int i = n - 1; i >= 0; i--)
        {
            float sum = 0f;
            for (int j = i + 1; j < n; j++)
            {
                sum += M[i, j] * result[j];
            }
            result[i] = (x[i] - sum) / M[i, i];
        }

        return result;
    }
}