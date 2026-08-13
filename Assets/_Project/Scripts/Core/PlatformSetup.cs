using UnityEngine;

namespace Cirrus.Core
{
    /// <summary>
    /// Runtime settings that must be right before anything else runs, applied
    /// automatically so no scene can forget them.
    ///
    /// Both of these are mobile-specific traps rather than preferences:
    ///
    /// **targetFrameRate** — Unity defaults mobile to 30 fps. The whole performance
    /// budget in CLAUDE.md §6 is written against 60, so without this the first
    /// device run looks like a failure that isn't one.
    ///
    /// **maximumDeltaTime** — aero runs at 200 Hz (§4), so a frame that takes 100 ms
    /// would ask PhysX for twenty 5 ms steps at once, which takes longer than the
    /// frame that caused it: the classic spiral of death. Capping the catch-up at
    /// 50 ms means a hitch shows up as a brief slow-motion moment and recovers,
    /// instead of compounding. The 200 Hz rate itself is non-negotiable, so this is
    /// the knob that gives.
    /// </summary>
    public static class PlatformSetup
    {
        public const int TargetFrameRate = 60;
        public const float MaximumDeltaTime = 0.05f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply()
        {
            QualitySettings.vSyncCount = 0; // vSync overrides targetFrameRate where it applies
            Application.targetFrameRate = TargetFrameRate;
            Time.maximumDeltaTime = MaximumDeltaTime;

            // A flight sim is useless in portrait; lock to landscape both ways.
            Screen.orientation = ScreenOrientation.AutoRotation;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            Screen.sleepTimeout = SleepTimeout.NeverSleep; // long flights, few taps
        }
    }
}
