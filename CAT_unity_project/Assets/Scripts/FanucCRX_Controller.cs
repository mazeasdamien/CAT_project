using System.Collections.Generic;
using System.Text;
using UnityEngine;

// BASED ON: Carbonari et al. 2023 (Fanuc CRX Analytical IK)
public class FanucCRX_Controller : MonoBehaviour
{
    [Header("Target")]
    public Transform targetIK;

    [Header("Robot Joints (J1-J6)")]
    public List<Transform> robotJoints = new List<Transform>();

    [Header("Configuration")]
    [Tooltip("If true, the robot automatically picks the solution closest to its current pose (prevents jumping).")]
    public bool autoSelectClosest = true;

    [Tooltip("Manual selection if Auto is off")]
    [Range(0, 15)]
    public int manualSolutionIndex = 0;

    [Header("Calibration Offsets")]
    public float[] jointOffsets = new float[6];

    // Internal
    private List<Vector3> jointAxes = new List<Vector3>();
    private List<double[]> foundSolutions = new List<double[]>();

    // --- DH PARAMETERS (Meters, Radians) ---
    private static readonly double[,] DH_Params = new double[6, 3] {
        { Mathf.PI / 2.0,   0.0,        0.2503 }, // J1
        { -Mathf.PI,        0.710,      0.2604 }, // J2
        { -Mathf.PI / 2.0,  0.0,        0.2604 }, // J3
        { -Mathf.PI / 2.0,  0.0,        0.5400 }, // J4
        { Mathf.PI / 2.0,   0.0,        0.1500 }, // J5 (Offset)
        { 0.0,              0.0,        0.1600 }  // J6 (Tool)
    };

    private static readonly double[,] JointLimits = new double[6, 2] {
        { -Mathf.PI, Mathf.PI }, {-Mathf.PI, Mathf.PI }, {-4.71, 4.71 },
        { -3.33, 3.33 }, {-Mathf.PI, Mathf.PI }, {-3.92, 3.92 }
    };

    void Start()
    {
        // 1. HARDCODED AXES (As per your request)
        jointAxes.Clear();
        jointAxes.Add(Vector3.forward); // J1 (Z)
        jointAxes.Add(Vector3.up);      // J2 (Y)
        jointAxes.Add(Vector3.up);      // J3 (Y)
        jointAxes.Add(Vector3.right);   // J4 (X)
        jointAxes.Add(Vector3.up);      // J5 (Y)
        jointAxes.Add(Vector3.right);   // J6 (X)

        // 2. DEFAULT OFFSETS (Common Fanuc Corrections)
        if (jointOffsets[1] == 0) jointOffsets[1] = -90f; // J2 Vertical Correction
    }

    void Update()
    {
        if (robotJoints.Count != 6 || targetIK == null) return;
        SolveAnalyticalIK();
        ApplySolution();
    }

    private void SolveAnalyticalIK()
    {
        foundSolutions.Clear();

        // 1. Transform Target to Local Robot Frame
        Matrix4x4 targetMatrix = transform.worldToLocalMatrix * targetIK.localToWorldMatrix;
        Vector3 P_target = new Vector3(targetMatrix.m03, targetMatrix.m13, targetMatrix.m23);
        Vector3 Z_target = new Vector3(targetMatrix.m02, targetMatrix.m12, targetMatrix.m22).normalized;

        // 2. Wrist Center (W) calculation for Offset Wrist
        double d6 = DH_Params[5, 2];
        Vector3 W = P_target - Z_target * (float)d6;

        // 3. Solve J1
        double[] theta1_candidates = new double[2];
        theta1_candidates[0] = Mathf.Atan2(W.y, W.x);
        theta1_candidates[1] = Mathf.Atan2(-W.y, -W.x);

        foreach (double th1 in theta1_candidates)
        {
            Matrix4x4 T1 = GetDHMatrix(0, th1);
            Vector3 W_1 = T1.inverse.MultiplyPoint3x4(W);

            double d4 = DH_Params[3, 2], d5 = DH_Params[4, 2], a2 = DH_Params[1, 1];
            double L_forearm = System.Math.Sqrt(d4 * d4 + d5 * d5);
            double r_sq = W_1.x * W_1.x + W_1.y * W_1.y;
            double r = System.Math.Sqrt(r_sq);

            // Reach Check
            if (r > (a2 + L_forearm) + 0.001 || r < System.Math.Abs(a2 - L_forearm) - 0.001) continue;

            // Solve J3
            double cosPhi = (a2 * a2 + L_forearm * L_forearm - r_sq) / (2 * a2 * L_forearm);
            cosPhi = Mathf.Clamp((float)cosPhi, -1f, 1f);
            double phi = System.Math.Acos(cosPhi);
            double beta = System.Math.Atan2(d5, d4);

            double[] theta3_candidates = new double[] { (Mathf.PI - phi) - beta, -(Mathf.PI - phi) - beta };

            foreach (double th3 in theta3_candidates)
            {
                // Solve J2
                double alpha_r = System.Math.Atan2(W_1.y, W_1.x);
                double cosGamma = (a2 * a2 + r_sq - L_forearm * L_forearm) / (2 * a2 * r);
                cosGamma = Mathf.Clamp((float)cosGamma, -1f, 1f);
                double gamma = System.Math.Acos(cosGamma);

                double th2 = (th3 == theta3_candidates[0]) ? (alpha_r - gamma) : (alpha_r + gamma);

                // Solve J4
                Matrix4x4 T03 = T1 * GetDHMatrix(1, th2) * GetDHMatrix(2, th3);
                Vector3 W_3 = T03.inverse.MultiplyPoint3x4(W);
                double th4 = System.Math.Atan2(-W_3.x, W_3.y);
                double[] theta4_candidates = new double[] { th4, th4 + Mathf.PI };

                foreach (double th4_c in theta4_candidates)
                {
                    // Solve J5, J6
                    Matrix4x4 T04 = T03 * GetDHMatrix(3, th4_c);
                    Matrix4x4 R_target = Matrix4x4.Rotate(Quaternion.Inverse(transform.rotation) * targetIK.rotation);
                    Matrix4x4 R46 = GetRotationMatrix(T04).transpose * R_target;

                    double c5 = R46.m22;
                    double s5 = System.Math.Sqrt(1 - c5 * c5); // Sqrt ensures positive sine first

                    // J5 can be + or - (Flip)
                    double[] theta5_candidates = new double[] { System.Math.Atan2(s5, c5), System.Math.Atan2(-s5, c5) };

                    foreach (double th5_c in theta5_candidates)
                    {
                        double th6 = System.Math.Atan2(R46.m21, -R46.m20);

                        double[] solution = new double[] { th1, th2, th3, th4_c, th5_c, th6 };
                        if (ValidateLimits(solution)) foundSolutions.Add(solution);
                    }
                }
            }
        }
    }

    private void ApplySolution()
    {
        if (foundSolutions.Count == 0) return;

        double[] bestSol;

        if (autoSelectClosest)
        {
            // --- STABILIZER: Pick solution closest to current joints ---
            int bestIndex = 0;
            double minDiff = double.MaxValue;

            for (int i = 0; i < foundSolutions.Count; i++)
            {
                double diff = 0;
                for (int j = 0; j < 6; j++)
                {
                    // Get current angle from Unity (normalized -PI to PI)
                    float currentDeg = 0;
                    if (jointAxes[j] == Vector3.forward) currentDeg = robotJoints[j].localEulerAngles.z;
                    else if (jointAxes[j] == Vector3.up) currentDeg = robotJoints[j].localEulerAngles.y;
                    else if (jointAxes[j] == Vector3.right) currentDeg = robotJoints[j].localEulerAngles.x;

                    // Convert to Rads and Remove Offset to match DH
                    double currentRad = (currentDeg > 180 ? currentDeg - 360 : currentDeg) * Mathf.Deg2Rad;
                    currentRad -= (jointOffsets[j] * Mathf.Deg2Rad);

                    // Normalize Math Angle diff
                    double d = System.Math.Abs(NormalizeRad(foundSolutions[i][j]) - NormalizeRad(currentRad));
                    diff += d;
                }
                if (diff < minDiff) { minDiff = diff; bestIndex = i; }
            }
            bestSol = foundSolutions[bestIndex];
        }
        else
        {
            // Manual Select
            int idx = Mathf.Clamp(manualSolutionIndex, 0, foundSolutions.Count - 1);
            bestSol = foundSolutions[idx];
        }

        // Apply
        for (int i = 0; i < 6; i++)
        {
            float deg = (float)(bestSol[i] * Mathf.Rad2Deg);
            deg += jointOffsets[i];
            robotJoints[i].localRotation = Quaternion.AngleAxis(deg, jointAxes[i]);
        }
    }

    // --- VISUAL DEBUGGING ---
    // Draws where the math THINKS the wrist is. 
    // If this red sphere isn't on your target, your units/transform are wrong.
    void OnDrawGizmos()
    {
        if (targetIK == null) return;
        Matrix4x4 targetMatrix = transform.worldToLocalMatrix * targetIK.localToWorldMatrix;
        Vector3 P_target = new Vector3(targetMatrix.m03, targetMatrix.m13, targetMatrix.m23);
        Vector3 Z_target = new Vector3(targetMatrix.m02, targetMatrix.m12, targetMatrix.m22).normalized;
        double d6 = DH_Params[5, 2];
        Vector3 W = P_target - Z_target * (float)d6;

        // Convert back to world to draw
        Vector3 W_World = transform.TransformPoint(W);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(W_World, 0.05f); // Wrist Center
        Gizmos.color = Color.green;
        Gizmos.DrawLine(W_World, targetIK.position); // Tool Offset
    }

    // --- Helpers ---
    private Matrix4x4 GetDHMatrix(int i, double theta)
    {
        float alpha = (float)DH_Params[i, 0], a = (float)DH_Params[i, 1], d = (float)DH_Params[i, 2], th = (float)theta;
        float cTh = Mathf.Cos(th), sTh = Mathf.Sin(th), cAl = Mathf.Cos(alpha), sAl = Mathf.Sin(alpha);
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = cTh; m.m01 = -sTh * cAl; m.m02 = sTh * sAl; m.m03 = a * cTh;
        m.m10 = sTh; m.m11 = cTh * cAl; m.m12 = -cTh * sAl; m.m13 = a * sTh;
        m.m20 = 0; m.m21 = sAl; m.m22 = cAl; m.m23 = d;
        m.m33 = 1; return m;
    }
    private Matrix4x4 GetRotationMatrix(Matrix4x4 m) { m.m03 = 0; m.m13 = 0; m.m23 = 0; return m; }
    private double NormalizeRad(double ang)
    {
        while (ang > System.Math.PI) ang -= 2 * System.Math.PI;
        while (ang < -System.Math.PI) ang += 2 * System.Math.PI;
        return ang;
    }
    private bool ValidateLimits(double[] a) { return true; } // Keeping it simple
}