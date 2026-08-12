using UnityEngine;

namespace Cirrus.CameraRigs
{
    /// <summary>
    /// M1 chase camera: smoothed follow behind the aircraft, blending between the
    /// nose direction and the velocity vector so sideslip and stalls read visually.
    /// Cockpit and cinematic rigs come later (M4 / M3).
    /// </summary>
    public sealed class ChaseCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target = null!;
        [SerializeField] private Rigidbody _targetBody = null!;
        [SerializeField] private float _distance = 14f;
        [SerializeField] private float _height = 4f;
        [SerializeField] private float _positionSmoothTime = 0.35f;
        [SerializeField] private float _velocityBlend = 0.35f; // 0 = follow nose, 1 = follow velocity

        Vector3 _positionVelocity;

        public void SetTarget(Transform target, Rigidbody body)
        {
            _target = target;
            _targetBody = body;
            transform.position = DesiredPosition();
        }

        void LateUpdate()
        {
            if (_target == null) return;

            transform.position = Vector3.SmoothDamp(
                transform.position, DesiredPosition(), ref _positionVelocity, _positionSmoothTime);
            transform.rotation = Quaternion.LookRotation(
                (_target.position + _target.forward * 6f - transform.position).normalized, Vector3.up);
        }

        Vector3 DesiredPosition()
        {
            Vector3 forward = _target.forward;
            if (_targetBody != null && _targetBody.linearVelocity.sqrMagnitude > 25f)
                forward = Vector3.Slerp(forward, _targetBody.linearVelocity.normalized, _velocityBlend);

            Vector3 flatForward = forward;
            flatForward.y *= 0.5f; // keep the camera from diving under the aircraft in climbs/descents
            flatForward.Normalize();
            return _target.position - flatForward * _distance + Vector3.up * _height;
        }
    }
}
