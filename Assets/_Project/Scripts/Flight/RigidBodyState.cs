using System.Numerics;

namespace Cirrus.Flight
{
    /// <summary>
    /// Kinematic state of the aircraft.
    /// World frame: X forward/north-ish, Y right/east-ish, Z up, right-handed.
    /// Body frame: X out the nose, Y out the right wing, Z up through the roof.
    /// Orientation rotates body-frame vectors into the world frame.
    /// Angular velocity is expressed in the BODY frame (convert at the Unity boundary).
    /// </summary>
    public struct RigidBodyState
    {
        public Vector3 Position;        // m, world
        public Vector3 Velocity;        // m/s, world
        public Quaternion Orientation;  // body -> world
        public Vector3 AngularVelocity; // rad/s, body frame

        public static RigidBodyState LevelFlight(float speed, float altitude, float pitchAngle)
        {
            // Positive rotation about +Y pitches the nose DOWN in this frame,
            // so a nose-up attitude is a rotation of -pitchAngle about +Y.
            return new RigidBodyState
            {
                Position = new Vector3(0f, 0f, altitude),
                Velocity = new Vector3(speed, 0f, 0f),
                Orientation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -pitchAngle),
                AngularVelocity = Vector3.Zero,
            };
        }
    }
}
