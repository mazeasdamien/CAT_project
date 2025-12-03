using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Implements the Geometric Inverse Kinematics approach for FANUC CRX-10iA/L
/// Based on "Geometric Approach for Inverse Kinematics of the FANUC CRX Collaborative Robot" (Abbes & Poisson, 2024).
/// </summary>
public class FanucCRXKinematics
{
    // --- DHm Parameters for CRX-10iA/L ---
    // The L version has a longer Link 2 (a2) than the standard version (710mm vs 540mm).
    private const double a2 = 710.0; // Link 2 Length (L3 in PDF Table 2)
    
    // Other parameters from Table 2
    private const double r5 = 150.0;
    private const double r6 = -160.0; 
    private const double r4 = -540.0; // Forearm length (Negative in DH table)
    
    // Constants
    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;

    public struct IKSolution
    {
        public double[] Joints; // J1 to J6 in degrees
        public bool IsValid;
    }

    /// <summary>
    /// Solves IK for a given position and orientation in Robot Coordinate Space (Right-Handed, Z-Up).
    /// </summary>
    /// <param name="targetPos">Target Position (X, Y, Z) in millimeters.</param>
    /// <param name="targetRot">Target Rotation Matrix (3x3).</param>
    /// <returns>List of valid joint configurations.</returns>
    public List<IKSolution> SolveIK(Vector3d targetPos, Matrix4x4d targetRot)
    {
        List<IKSolution> solutions = new List<IKSolution>();

        // Step 1: Position Centers O6 (TCP) and O5 (Wrist Center)
        // O6 is the Target Position
        Vector3d O6 = targetPos;
        
        // Calculate O5 (Wrist Center). 
        // In the tool frame, O5 is at [0, 0, r6]. Transformed to world:
        // O5 = O6 - (R_tool * [0, 0, -r6]) -> Note the sign of r6 in the table is -160.
        // Geometrically, we back off along the Tool Z axis by 160mm.
        Vector3d Z_tool = new Vector3d(targetRot.m02, targetRot.m12, targetRot.m22);
        Vector3d O5 = O6 - (Z_tool * (-r6)); 

        // Step 2-5: Scan parameter q (angle around O5-O6 axis)
        // The PDF method scans the redundant angle 'q' to satisfy the constraint that Z4 must be perpendicular to Z5.
        // We scan 0-360 degrees to find valid configurations.
        
        int scanSteps = 72; // 5-degree increments for coarse search
        double prevResidueUP = 0;
        double prevResidueDW = 0;
        bool first = true;

        for (int i = 0; i <= scanSteps; i++)
        {
            double q = i * (360.0 / scanSteps) * Deg2Rad;
            
            // We check both "Elbow Up" and "Elbow Down" configurations for each q
            double resUP = CalculateResidue(q, O5, O6, targetRot, true, out var solUP);
            double resDW = CalculateResidue(q, O5, O6, targetRot, false, out var solDW);

            if (!first)
            {
                // Check for zero crossing (change of sign in residue)
                if (Math.Sign(resUP) != Math.Sign(prevResidueUP))
                {
                    double qZero = RefineRoot(q - (360.0/scanSteps)*Deg2Rad, q, O5, O6, targetRot, true);
                    CalculateResidue(qZero, O5, O6, targetRot, true, out var finalSol);
                    if(finalSol.IsValid) solutions.Add(finalSol);
                }
                if (Math.Sign(resDW) != Math.Sign(prevResidueDW))
                {
                    double qZero = RefineRoot(q - (360.0/scanSteps)*Deg2Rad, q, O5, O6, targetRot, false);
                    CalculateResidue(qZero, O5, O6, targetRot, false, out var finalSol);
                    if(finalSol.IsValid) solutions.Add(finalSol);
                }
            }

            prevResidueUP = resUP;
            prevResidueDW = resDW;
            first = false;
        }

        return solutions;
    }

    // Binary search to find the exact 'q' where the kinematic chain closes (Z4.Z5 = 0)
    private double RefineRoot(double qMin, double qMax, Vector3d O5, Vector3d O6, Matrix4x4d R_tool, bool isUp)
    {
        for(int i=0; i<10; i++)
        {
            double mid = (qMin + qMax) / 2.0;
            double valMid = CalculateResidue(mid, O5, O6, R_tool, isUp, out _);
            double valMin = CalculateResidue(qMin, O5, O6, R_tool, isUp, out _);

            if (Math.Sign(valMid) == Math.Sign(valMin)) qMin = mid;
            else qMax = mid;
        }
        return (qMin + qMax) / 2.0;
    }

    // Calculates the "Residue" (Dot product Z4.Z5) for a given q.
    // If residue is 0, the configuration is valid.
    private double CalculateResidue(double q, Vector3d O5, Vector3d O6, Matrix4x4d R_tool, bool isUp, out IKSolution solution)
    {
        solution = new IKSolution { IsValid = false, Joints = new double[6] };

        // --- Step 2: Determine O4(q) ---
        // Create a coordinate system on the wrist axis (O5-O6)
        Vector3d z_axis = (O6 - O5).Normalized();
        Vector3d tempY = Math.Abs(Vector3d.Dot(z_axis, Vector3d.Up)) > 0.9 ? Vector3d.Right : Vector3d.Up;
        Vector3d x_axis = Vector3d.Cross(tempY, z_axis).Normalized();
        Vector3d y_axis = Vector3d.Cross(z_axis, x_axis).Normalized();

        // O4 revolves around O5. Radius is r5 (150mm).
        Vector3d O4 = O5 + (x_axis * Math.Cos(q) * r5) + (y_axis * Math.Sin(q) * r5);

        // --- Step 3: Determine O3 (Elbow) and J1, J2, J3 ---
        double d04 = O4.Magnitude();
        
        // Triangle reachability check
        // Triangle sides: a2 (710), r4 (540), d04 (Distance origin to O4)
        if (d04 > (a2 + Math.Abs(r4)) || d04 < Math.Abs(a2 - Math.Abs(r4))) 
            return 100.0; // Out of reach

        // J1: Angle to O4 in XY plane
        double J1 = Math.Atan2(O4.y, O4.x);

        // Solve Triangle in the vertical plane defined by J1
        double X_prime = Math.Sqrt(O4.x*O4.x + O4.y*O4.y);
        double Z_prime = O4.z;
        
        // Cosine Law for Angle at Shoulder
        double numer = (X_prime*X_prime + Z_prime*Z_prime + a2*a2 - r4*r4);
        double denom = (2 * a2 * Math.Sqrt(X_prime*X_prime + Z_prime*Z_prime));
        double cosAlpha = numer / denom;
        
        if (Math.Abs(cosAlpha) > 1.0) return 100.0;
        
        double alpha = Math.Acos(cosAlpha);
        double beta = Math.Atan2(Z_prime, X_prime);
        
        // J2 (Geometric)
        double J2_rad = beta - (isUp ? alpha : -alpha);
        
        // J3 (Geometric) -> Angle at Elbow
        double cosGamma = (a2*a2 + r4*r4 - (X_prime*X_prime + Z_prime*Z_prime)) / (2 * a2 * Math.Abs(r4));
        if (Math.Abs(cosGamma) > 1.0) return 100.0;
        double gamma = Math.Acos(cosGamma);
        
        double J3_rad = Math.PI - gamma;
        if (!isUp) J3_rad = -J3_rad;

        // Apply DH Parameter offsets (Table 2)
        // Theta2 = J2 - 90  => J2_fanuc = Theta2 + 90
        // But we computed geometric angles. Let's align with PDF Eq 19.
        // PDF Eq 19 accounts for the specific Fanuc J2/J3 coupling implicitly.
        // Let's implement PDF Eq 19 exactly to be safe.
        
        double u1 = Math.Atan2(O4.z, Math.Sqrt(O4.x*O4.x + O4.y*O4.y));
        double u2_numer = (O4.x*O4.x + O4.y*O4.y + O4.z*O4.z) - (a2*a2 + r4*r4);
        double u2_denom = 2 * a2 * r4; // r4 is -540
        double u2 = -(u2_numer / u2_denom);
        
        if (Math.Abs(u2) > 1.0) return 100.0;

        double delta = isUp ? -1.0 : 1.0; 
        double u3 = delta * Math.Acos(u2);
        double u4 = Math.Atan2(-r4 * Math.Sin(u3), a2 - r4 * Math.Cos(u3));
        
        double J2_final = (Math.PI/2.0 - u1 + u4) * Rad2Deg;
        double J3_final = (u1 + u3 - u4) * Rad2Deg;

        // --- Step 4/5: Orientation (J4, J5, J6) ---
        // Calculate Forward Kinematics for J1-J3 to get Frame 4 Position/Orientation
        Matrix4x4d T1 = DH(0, 0, 0, J1*Rad2Deg);
        Matrix4x4d T2 = DH(-90, 0, 0, J2_final - 90);
        Matrix4x4d T3 = DH(180, a2, 0, J2_final + J3_final); 
        Matrix4x4d T3_global = T1 * T2 * T3;
        
        // Determine J4
        // J4 is angle of vector O5-O4 in Frame 3? No.
        // We use the matrix inversion method to find J4, J5, J6 that match R_tool.
        
        // O5 and O4 are known. V = O5 - O4.
        // Transform V into Frame 3.
        Matrix4x4d T3_inv = T3_global.Inverse();
        Vector3d O4_in3 = T3_inv.MultiplyPoint(O4);
        Vector3d O5_in3 = T3_inv.MultiplyPoint(O5);
        Vector3d V = O5_in3 - O4_in3;

        double J4_rad = Math.Atan2(V.y, V.x);
        double J4_final = J4_rad * Rad2Deg;

        // Compute Frame 4
        Matrix4x4d T4 = DH(-90, 0, r4, J4_final); // Note r4 is in 'r' column
        Matrix4x4d T4_global = T3_global * T4;
        
        // Calculate Remaining Rotation Matrix R_46 = Inv(R4) * R_target
        // This matrix represents Rz(J5) * Rx(90) * Rz(J6) * ...
        Matrix4x4d R4_inv = T4_global.Inverse();
        Matrix4x4d R46 = R4_inv * R_tool;

        // Based on DH Table:
        // 4T5: theta=J5, alpha=90
        // 5T6: theta=J6, alpha=-90
        // Resulting rotation matrix has specific terms.
        // R46 = [ c5c6, -c5s6, s5 ] ... 
        
        // Residue: Z4 . Z5 check.
        // In our derived matrix R46, element m21 should be 0 for valid geometry.
        // But simpler: just extract Euler angles.
        
        double J5_rad = Math.Atan2(-R46.m01, R46.m11);
        double J6_rad = Math.Atan2(R46.m20, R46.m22);
        
        // The residue that must be zero is derived from the structural constraint.
        // If the wrist can't physically align, R46 won't match the form [c5c6...].
        // The term R46.m21 represents the error in orthogonality.
        double residue = R46.m21;

        solution.Joints[0] = J1 * Rad2Deg;
        solution.Joints[1] = J2_final;
        solution.Joints[2] = J3_final;
        solution.Joints[3] = J4_final;
        solution.Joints[4] = J5_rad * Rad2Deg;
        solution.Joints[5] = J6_rad * Rad2Deg;
        solution.IsValid = true;

        return residue;
    }

    // Standard Modified DH Matrix Generation
    private Matrix4x4d DH(double alpha, double a, double r, double theta)
    {
        double tr = theta * Deg2Rad;
        double ar = alpha * Deg2Rad;
        double ct = Math.Cos(tr);
        double st = Math.Sin(tr);
        double ca = Math.Cos(ar);
        double sa = Math.Sin(ar);

        Matrix4x4d m = new Matrix4x4d();
        m.m00 = ct;       m.m01 = -st;      m.m02 = 0;    m.m03 = a;
        m.m10 = st*ca;    m.m11 = ct*ca;    m.m12 = -sa;  m.m13 = -r*sa;
        m.m20 = st*sa;    m.m21 = ct*sa;    m.m22 = ca;   m.m23 = r*ca;
        m.m30 = 0;        m.m31 = 0;        m.m32 = 0;    m.m33 = 1;
        return m;
    }
}

// Double Precision Math Structs for Accuracy
public struct Vector3d
{
    public double x, y, z;
    public static Vector3d Up => new Vector3d(0, 1, 0);
    public static Vector3d Right => new Vector3d(1, 0, 0);
    public Vector3d(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
    
    public static Vector3d operator +(Vector3d a, Vector3d b) => new Vector3d(a.x + b.x, a.y + b.y, a.z + b.z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new Vector3d(a.x - b.x, a.y - b.y, a.z - b.z);
    public static Vector3d operator *(Vector3d a, double b) => new Vector3d(a.x * b, a.y * b, a.z * b);
    public double Magnitude() => Math.Sqrt(x*x + y*y + z*z);
    public Vector3d Normalized() { double m = Magnitude(); return m > 0 ? this * (1/m) : this; }
    public static double Dot(Vector3d a, Vector3d b) => a.x * b.x + a.y * b.y + a.z * b.z;
    public static Vector3d Cross(Vector3d a, Vector3d b) => new Vector3d(a.y*b.z - a.z*b.y, a.z*b.x - a.x*b.z, a.x*b.y - a.y*b.x);
}

public struct Matrix4x4d
{
    public double m00, m01, m02, m03, m10, m11, m12, m13, m20, m21, m22, m23, m30, m31, m32, m33;

    public static Matrix4x4d Identity() { var m = new Matrix4x4d(); m.m00=1; m.m11=1; m.m22=1; m.m33=1; return m; }

    public static Matrix4x4d operator *(Matrix4x4d a, Matrix4x4d b)
    {
        var r = new Matrix4x4d();
        r.m00 = a.m00*b.m00 + a.m01*b.m10 + a.m02*b.m20 + a.m03*b.m30;
        r.m01 = a.m00*b.m01 + a.m01*b.m11 + a.m02*b.m21 + a.m03*b.m31;
        r.m02 = a.m00*b.m02 + a.m01*b.m12 + a.m02*b.m22 + a.m03*b.m32;
        r.m03 = a.m00*b.m03 + a.m01*b.m13 + a.m02*b.m23 + a.m03*b.m33;
        
        r.m10 = a.m10*b.m00 + a.m11*b.m10 + a.m12*b.m20 + a.m13*b.m30;
        r.m11 = a.m10*b.m01 + a.m11*b.m11 + a.m12*b.m21 + a.m13*b.m31;
        r.m12 = a.m10*b.m02 + a.m11*b.m12 + a.m12*b.m22 + a.m13*b.m32;
        r.m13 = a.m10*b.m03 + a.m11*b.m13 + a.m12*b.m23 + a.m13*b.m33;

        r.m20 = a.m20*b.m00 + a.m21*b.m10 + a.m22*b.m20 + a.m23*b.m30;
        r.m21 = a.m20*b.m01 + a.m21*b.m11 + a.m22*b.m21 + a.m23*b.m31;
        r.m22 = a.m20*b.m02 + a.m21*b.m12 + a.m22*b.m22 + a.m23*b.m32;
        r.m23 = a.m20*b.m03 + a.m21*b.m13 + a.m22*b.m23 + a.m23*b.m33;
        r.m33 = 1; return r;
    }

    public Vector3d MultiplyPoint(Vector3d v) => new Vector3d(
        m00 * v.x + m01 * v.y + m02 * v.z + m03,
        m10 * v.x + m11 * v.y + m12 * v.z + m13,
        m20 * v.x + m21 * v.y + m22 * v.z + m23
    );
    
    public Matrix4x4d Inverse()
    {
        var r = new Matrix4x4d();
        // Transpose Rotation
        r.m00 = m00; r.m01 = m10; r.m02 = m20;
        r.m10 = m01; r.m11 = m11; r.m12 = m21;
        r.m20 = m02; r.m21 = m12; r.m22 = m22;
        // Inverse Translation
        double tx = m03, ty = m13, tz = m23;
        r.m03 = -(r.m00 * tx + r.m01 * ty + r.m02 * tz);
        r.m13 = -(r.m10 * tx + r.m11 * ty + r.m12 * tz);
        r.m23 = -(r.m20 * tx + r.m21 * ty + r.m22 * tz);
        r.m33 = 1; return r;
    }
}