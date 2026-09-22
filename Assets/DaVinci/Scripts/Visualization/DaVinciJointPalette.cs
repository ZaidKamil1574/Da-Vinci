using UnityEngine;

namespace XRMultiplayer.DaVinci
{
    /// <summary>
    /// The colour each joint of the Da Vinci model is drawn in, shared by the angle wedges, the
    /// force arrows and the telemetry graphs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One table rather than a colour picked separately in each place. A graph whose yellow trace
    /// belongs to a joint wearing a blue wedge in the world is worse than no colour at all — the
    /// reader has to hold a second mapping in their head to use either. Sharing the table makes
    /// "the yellow one" mean the same thing everywhere.
    /// </para>
    /// <para>
    /// Matched by name prefix, so <c>HAND_1</c> and <c>HAND_1.003</c> — the same link on two
    /// different arms — come out the same colour. That is the useful grouping here: it lets you
    /// compare the same joint across arms, which is what the four arms of this machine are for.
    /// </para>
    /// </remarks>
    public static class DaVinciJointPalette
    {
        /// <summary>Colour for a joint whose name matches no entry.</summary>
        public static readonly Color fallback = new Color(0.62f, 0.68f, 0.78f);

        /// <summary>
        /// Name prefixes paired with their colour, longest prefix first.
        /// </summary>
        /// <remarks>
        /// Order matters: "ROTARY MECHANISM_1" has to be tested before any shorter prefix that
        /// would also match it, or the wrong entry wins.
        /// </remarks>
        static readonly (string prefix, Color colour)[] k_Table =
        {
            ("HAND_BEGIN",           new Color(1.00f, 0.45f, 0.85f)),
            ("ROTARY MECHANISM_1",   new Color(0.20f, 0.90f, 0.90f)),
            ("ROTARY MECHANISM_2",   new Color(1.00f, 0.55f, 0.15f)),
            ("ROTARY MECHANISM_3",   new Color(0.70f, 0.50f, 1.00f)),
            ("HAND_1",               new Color(1.00f, 0.85f, 0.10f)),
            ("HAND_2",               new Color(0.95f, 0.25f, 0.25f)),
            ("HAND_3",               new Color(0.30f, 0.90f, 0.35f)),
            ("HAND_4",               new Color(0.30f, 0.60f, 1.00f)),
            ("HAND_5",               new Color(0.95f, 0.95f, 0.95f)),
            ("HAND_6",               new Color(0.55f, 0.80f, 0.75f)),
            ("HAND_7",               new Color(0.85f, 0.65f, 0.45f)),
        };

        /// <summary>The colour for <paramref name="jointName"/>, or <see cref="fallback"/>.</summary>
        public static Color For(string jointName)
        {
            if (string.IsNullOrEmpty(jointName))
                return fallback;

            for (var i = 0; i < k_Table.Length; i++)
            {
                if (jointName.StartsWith(k_Table[i].prefix, System.StringComparison.OrdinalIgnoreCase))
                    return k_Table[i].colour;
            }

            return fallback;
        }
    }
}
