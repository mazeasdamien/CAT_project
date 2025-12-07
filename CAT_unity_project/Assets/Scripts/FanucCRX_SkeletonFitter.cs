using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class FanucCRX_SkeletonFitter : MonoBehaviour
{
    [Header("1. Copy Values from Previous Step")]
    public float scaleFactor = 1.0f;
    public Vector3 baseOffset = Vector3.zero;
    public Vector3 baseRotation = Vector3.zero;

    [Header("2. Fit Skeleton to Mesh (Find these Offsets!)")]
    [Range(-180, 180)] public float offsetJ2 = 0f;
    [Range(-180, 180)] public float offsetJ3 = 0f;
    [Range(-180, 180)] public float offsetJ4 = 0f;
    [Range(-180, 180)] public float offsetJ5 = 0f;

    [Header("3. Debug Visuals")]
    public bool showSkeleton = true;

    // DH Params (Carbonari 2023)
    private static readonly double[,] DH_Base = new double[6, 3] {
        { Mathf.PI / 2.0,   0.0,        0.2503 }, // J1
        { -Mathf.PI,        0.710,      0.2604 }, // J2
        { -Mathf.PI / 2.0,  0.0,        0.2604 }, // J3
        { -Mathf.PI / 2.0,  0.0,        0.5400 }, // J4
        { Mathf.PI / 2.0,   0.0,        0.1500 }, // J5
        { 0.0,              0.0,        0.1600 }  // J6
    };

    private List<Vector3> points = new List<Vector3>();

    void Update()
    {
        points.Clear();

        // Start at Base
        Matrix4x4 T = Matrix4x4.TRS(
            transform.position + transform.TransformVector(baseOffset),
            transform.rotation * Quaternion.Euler(baseRotation),
            Vector3.one
        );
        points.Add(T.MultiplyPoint(Vector3.zero));

        // Offsets array
        float[] offs = new float[] { 0, offsetJ2, offsetJ3, offsetJ4, offsetJ5, 0 };

        for (int i = 0; i < 6; i++)
        {
            float th = offs[i] * Mathf.Deg2Rad; // Angle = Offset (since TestAngle is 0)
            float alpha = (float)DH_Base[i, 0];
            float a = (float)DH_Base[i, 1] * scaleFactor;
            float d = (float)DH_Base[i, 2] * scaleFactor;

            // DH Matrix
            Matrix4x4 dh = Matrix4x4.identity;
            float cT = Mathf.Cos(th), sT = Mathf.Sin(th);
            float cA = Mathf.Cos(alpha), sA = Mathf.Sin(alpha);

            dh.m00 = cT; dh.m01 = -sT * cA; dh.m02 = sT * sA; dh.m03 = a * cT;
            dh.m10 = sT; dh.m11 = cT * cA; dh.m12 = -cT * sA; dh.m13 = a * sT;
            dh.m20 = 0; dh.m21 = sA; dh.m22 = cA; dh.m23 = d;

            T = T * dh;
            points.Add(T.MultiplyPoint(Vector3.zero));
        }
    }

    void OnDrawGizmos()
    {
        if (!showSkeleton || points.Count == 0) return;

        for (int i = 0; i < points.Count - 1; i++)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(points[i], points[i + 1]);
            Gizmos.color = (i == 0 ? Color.red : (i == 1 ? Color.green : (i == 2 ? Color.blue : Color.cyan)));
            Gizmos.DrawWireSphere(points[i + 1], 0.03f * scaleFactor);
        }
    }
}