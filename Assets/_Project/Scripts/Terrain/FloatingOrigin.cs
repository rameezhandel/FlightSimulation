using System;
using System.Collections.Generic;
using Cirrus.Terrain;
using UnityEngine;

namespace Cirrus.TerrainStreaming
{
    /// <summary>
    /// Unity side of the floating origin (CLAUDE.md §5): watches the focus object
    /// (the aircraft) and, past OriginTracker.RebaseDistance from the Unity origin,
    /// shifts every registered root back so coordinates stay small. EVERY system
    /// that caches world positions must either live under a registered root or
    /// subscribe to Shifted — this is the single easiest way to introduce subtle,
    /// awful bugs; treat it seriously.
    /// </summary>
    public sealed class FloatingOrigin : MonoBehaviour
    {
        /// <summary>Unity-frame offset that was just added to every registered root.</summary>
        public event Action<UnityEngine.Vector3>? Shifted;

        public OriginTracker Tracker { get; private set; } = new OriginTracker(new LocalNE(0f, 0f));

        Transform? _focus;
        readonly List<Transform> _roots = new List<Transform>(16);

        public void Initialize(in LocalNE startRegionPosition, Transform focus)
        {
            Tracker = new OriginTracker(startRegionPosition);
            _focus = focus;
        }

        public void Register(Transform root)
        {
            if (!_roots.Contains(root)) _roots.Add(root);
        }

        public void Unregister(Transform root) => _roots.Remove(root);

        /// <summary>Region coordinates of a Unity world position (unity z = north, x = east).</summary>
        public LocalNE ToRegion(in UnityEngine.Vector3 unityPosition)
            => Tracker.ToRegion(unityPosition.z, unityPosition.x);

        /// <summary>Unity world position (horizontal) of region coordinates, at height y.</summary>
        public UnityEngine.Vector3 ToUnity(in LocalNE region, float y)
        {
            (float north, float east) = Tracker.ToLocal(region);
            return new UnityEngine.Vector3(east, y, north);
        }

        void LateUpdate()
        {
            if (_focus == null) return;
            UnityEngine.Vector3 p = _focus.position;
            if (!Tracker.NeedsRebase(p.z, p.x)) return;

            (float shiftNorth, float shiftEast) = Tracker.Rebase(p.z, p.x);
            var shift = new UnityEngine.Vector3(shiftEast, 0f, shiftNorth);

            for (int i = 0; i < _roots.Count; i++)
            {
                Transform root = _roots[i];
                if (root == null) continue;
                root.position += shift;
            }
            // Push the teleports into PhysX before the next physics step; velocities
            // are unaffected, which is the whole point of a pure translation.
            Physics.SyncTransforms();
            Shifted?.Invoke(shift);
        }
    }
}
