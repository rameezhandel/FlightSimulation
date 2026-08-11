using Cirrus.Flight;

namespace Cirrus.Core
{
    /// <summary>
    /// THE one and only Unity &lt;-&gt; flight-core frame conversion
    /// (docs/decisions/0001-pure-csharp-aero-core.md — never convert ad hoc elsewhere).
    ///
    /// Core frame:  X out the nose, Y out the right wing, Z up.
    /// Unity frame: x right, y up, z forward.
    /// Both libraries use identical component-wise vector/quaternion algebra, and the
    /// axis mapping is a pure cyclic permutation (determinant +1), so vectors, angular
    /// velocities, and quaternion vector parts all convert with the same swizzle:
    ///   core(X,Y,Z) = unity(z, x, y)   |   unity(x,y,z) = core(Y, Z, X)
    /// </summary>
    public static class CoreFrame
    {
        public static System.Numerics.Vector3 ToCore(in UnityEngine.Vector3 v)
            => new System.Numerics.Vector3(v.z, v.x, v.y);

        public static UnityEngine.Vector3 ToUnity(in System.Numerics.Vector3 v)
            => new UnityEngine.Vector3(v.Y, v.Z, v.X);

        public static System.Numerics.Quaternion ToCore(in UnityEngine.Quaternion q)
            => new System.Numerics.Quaternion(q.z, q.x, q.y, q.w);

        public static UnityEngine.Quaternion ToUnity(in System.Numerics.Quaternion q)
            => new UnityEngine.Quaternion(q.Y, q.Z, q.X, q.W);

        /// <summary>
        /// Unity reports Rigidbody.angularVelocity in world axes; the core wants it in
        /// the body frame.
        /// </summary>
        public static System.Numerics.Vector3 AngularVelocityToCoreBody(
            in UnityEngine.Vector3 unityWorldAngularVelocity,
            in System.Numerics.Quaternion coreOrientation)
            => MathUtil.WorldToBody(coreOrientation, ToCore(unityWorldAngularVelocity));

        /// <summary>
        /// Principal inertia diagonal converts with the same axis permutation:
        /// Unity (x right, y up, z fwd) takes core (Iyy, Izz, Ixx).
        /// </summary>
        public static UnityEngine.Vector3 InertiaToUnity(in System.Numerics.Vector3 coreDiagonal)
            => new UnityEngine.Vector3(coreDiagonal.Y, coreDiagonal.Z, coreDiagonal.X);
    }
}
