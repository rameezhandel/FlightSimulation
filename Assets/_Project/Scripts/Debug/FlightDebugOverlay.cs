#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Text;
using Cirrus.Aircraft;
using Cirrus.Flight;
using UnityEngine;

namespace Cirrus.DebugTools
{
    /// <summary>
    /// M1 dev overlay: airspeed, altitude, AoA, VSI, controls, and per-surface
    /// forces (CLAUDE.md §10). IMGUI, dev builds only. Text is rebuilt at 10 Hz
    /// into a reused StringBuilder to keep per-frame allocation out of flight.
    /// </summary>
    public sealed class FlightDebugOverlay : MonoBehaviour
    {
        [SerializeField] private FlightBody _flight = null!;
        [SerializeField] private Rigidbody _body = null!;

        readonly StringBuilder _text = new StringBuilder(1024);
        string _cached = "";
        float _nextRefresh;
        GUIStyle? _style;

        public void SetTarget(FlightBody flight, Rigidbody body)
        {
            _flight = flight;
            _body = body;
        }

        void Update()
        {
            if (_flight == null || Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.1f;

            _text.Clear();
            float knots = _flight.TrueAirspeed * MathUtil.MetersPerSecondToKnots;
            float fpm = _body.linearVelocity.y * MathUtil.MetersPerSecondToFeetPerMinute;
            float aoaDeg = _flight.AngleOfAttack * MathUtil.RadToDeg;

            _text.Append("TAS ").Append((int)knots).Append(" kt   ALT ")
                 .Append((int)(_flight.transform.position.y * 3.28084f)).Append(" ft   VSI ")
                 .Append((int)fpm).Append(" fpm\n");
            _text.Append("AoA ").Append(aoaDeg.ToString("F1")).Append(" deg   THR ")
                 .Append((int)(_flight.Controls.Throttle * 100f)).Append("%   FLAP ")
                 .Append((int)(_flight.Controls.Flap * 100f)).Append("%   THRUST ")
                 .Append((int)_flight.LastLoads.Thrust).Append(" N\n");

            bool stalled = false;
            for (int i = 0; i < _flight.SurfaceForces.Length; i++)
            {
                ref readonly SurfaceForceInfo info = ref _flight.SurfaceForces[i];
                ref readonly AeroSurfaceDefinition def = ref _flight.Aircraft.Surfaces[i];
                if (info.AngleOfAttack > def.Airfoil.StallAnglePositive) stalled = true;
                _text.Append(def.Name).Append("  aoa ")
                     .Append((info.AngleOfAttack * MathUtil.RadToDeg).ToString("F1"))
                     .Append("  cl ").Append(info.LiftCoefficient.ToString("F2"))
                     .Append("  |F| ").Append((int)info.Force.Length()).Append(" N\n");
            }
            if (stalled) _text.Append("*** STALL ***\n");

            _cached = _text.ToString();
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                normal = { textColor = Color.white },
            };
            GUI.Label(new Rect(10f, 10f, 640f, Screen.height - 20f), _cached, _style);
        }
    }
}
#endif
