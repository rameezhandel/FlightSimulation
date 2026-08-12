using Cirrus.Core;
using Cirrus.Flight;
using NUnit.Framework;

namespace Cirrus.Tests.UnityGlue
{
    /// <summary>
    /// Pins the one Unity &lt;-&gt; core frame conversion (decision 0001). Runs only in
    /// the Unity editor (references UnityEngine), so it is excluded from the headless
    /// harness — Unity Test Runner executes it.
    /// </summary>
    [TestFixture]
    public class CoreFrameConversionTests
    {
        [Test]
        public void VectorAxesMapAsDocumented()
        {
            // unity forward (z) -> core X (nose); unity right (x) -> core Y; unity up (y) -> core Z.
            Assert.AreEqual(new System.Numerics.Vector3(1f, 0f, 0f), CoreFrame.ToCore(UnityEngine.Vector3.forward));
            Assert.AreEqual(new System.Numerics.Vector3(0f, 1f, 0f), CoreFrame.ToCore(UnityEngine.Vector3.right));
            Assert.AreEqual(new System.Numerics.Vector3(0f, 0f, 1f), CoreFrame.ToCore(UnityEngine.Vector3.up));
        }

        [Test]
        public void VectorRoundTripIsIdentity()
        {
            var v = new UnityEngine.Vector3(1.5f, -2.25f, 3.75f);
            Assert.AreEqual(v, CoreFrame.ToUnity(CoreFrame.ToCore(v)));
        }

        [Test]
        public void QuaternionConversionPreservesRotations()
        {
            // A physical yaw: Unity rotation of +90 deg about up takes forward -> right.
            // Converted to core, the same rotation must take the nose (X) to the right wing (Y).
            var unityYaw = UnityEngine.Quaternion.AngleAxis(90f, UnityEngine.Vector3.up);
            System.Numerics.Quaternion coreYaw = CoreFrame.ToCore(unityYaw);
            System.Numerics.Vector3 nose = MathUtil.BodyToWorld(coreYaw, new System.Numerics.Vector3(1f, 0f, 0f));
            Assert.AreEqual(0f, nose.X, 1e-5f);
            Assert.AreEqual(1f, nose.Y, 1e-5f);
            Assert.AreEqual(0f, nose.Z, 1e-5f);
        }

        [Test]
        public void QuaternionConversionMatchesVectorConversion()
        {
            // Rotate-then-convert must equal convert-then-rotate for arbitrary input.
            var unityRotation = UnityEngine.Quaternion.Euler(20f, -35f, 50f);
            var unityVector = new UnityEngine.Vector3(0.3f, -1.2f, 2.1f);

            UnityEngine.Vector3 rotatedInUnity = unityRotation * unityVector;
            System.Numerics.Vector3 rotatedInCore = MathUtil.BodyToWorld(
                CoreFrame.ToCore(unityRotation), CoreFrame.ToCore(unityVector));

            UnityEngine.Vector3 backInUnity = CoreFrame.ToUnity(rotatedInCore);
            Assert.AreEqual(rotatedInUnity.x, backInUnity.x, 1e-4f);
            Assert.AreEqual(rotatedInUnity.y, backInUnity.y, 1e-4f);
            Assert.AreEqual(rotatedInUnity.z, backInUnity.z, 1e-4f);
        }

        [Test]
        public void InertiaDiagonalPermutes()
        {
            var core = new System.Numerics.Vector3(5368f, 6929f, 11159f); // Ixx fwd, Iyy right, Izz up
            UnityEngine.Vector3 unity = CoreFrame.InertiaToUnity(core);
            Assert.AreEqual(6929f, unity.x);  // about unity x = right wing axis
            Assert.AreEqual(11159f, unity.y); // about unity y = up axis
            Assert.AreEqual(5368f, unity.z);  // about unity z = nose axis
        }
    }
}
