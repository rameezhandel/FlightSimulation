using System;

namespace Cirrus.Controls
{
    /// <summary>
    /// Auto-rudder. CLAUDE.md wants a realistic flight model with arcade-friendly
    /// onboarding, and rudder is exactly where that tension lives: a real Beaver
    /// needs feet, and a phone has no pedals worth the screen space for a beginner.
    ///
    /// Two terms, both of which a pilot would recognise:
    ///   adverse-yaw lead — rolling generates yaw the wrong way, so feed in rudder
    ///                      with aileron rather than waiting for the slip.
    ///   slip feedback    — proportional correction on measured sideslip, which
    ///                      also cleans up the powered-climb yaw the slipstream and
    ///                      P-factor produce.
    ///
    /// This ASSISTS; it never overrides. Manual rudder is added on top and the sum
    /// is clamped, so a player who wants to slip the aircraft sideways onto a gravel
    /// bar still can — the assist just gets outvoted.
    /// </summary>
    public struct TurnCoordinator
    {
        public float AdverseYawGain;   // rudder per unit aileron
        public float SlipGain;         // rudder per radian of sideslip
        public float Authority;        // hard cap on the assist's own contribution
        public bool Enabled;

        public static TurnCoordinator Default() => new TurnCoordinator
        {
            AdverseYawGain = 0.25f,
            SlipGain = 2.5f,
            Authority = 0.6f,
            Enabled = true,
        };

        /// <param name="aileronInput">Roll command, [-1, 1].</param>
        /// <param name="sideslipRadians">
        /// Positive when the relative wind comes from the right (body-frame lateral
        /// velocity is positive), which needs right rudder to centre.
        /// </param>
        /// <param name="manualRudder">Player's own rudder input, [-1, 1].</param>
        public float Rudder(float aileronInput, float sideslipRadians, float manualRudder = 0f)
        {
            if (!Enabled)
                return Math.Clamp(manualRudder, -1f, 1f);

            float assist = AdverseYawGain * aileronInput + SlipGain * sideslipRadians;
            assist = Math.Clamp(assist, -Authority, Authority);
            return Math.Clamp(assist + manualRudder, -1f, 1f);
        }
    }
}
