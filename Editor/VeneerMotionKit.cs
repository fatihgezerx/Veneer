using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// How much motion the kit should use for an element. The theme's motion setting is applied to the root of every tree as one of the classes
    /// <see cref="OffClass"/>, <see cref="SubtleClass"/> or <see cref="LivelyClass"/> (none = normal); animations written in code ask this class
    /// for a scale for their durations and distances, so one switch calms or stops all of them.
    /// </summary>
    public static class VeneerMotionKit
    {
        /// <summary>Root class: no animation.</summary>
        public const string OffClass = "vn-motion--off";

        /// <summary>Root class: short, small movements.</summary>
        public const string SubtleClass = "vn-motion--subtle";

        /// <summary>Root class: longer, bigger, springier movements.</summary>
        public const string LivelyClass = "vn-motion--lively";

        /// <summary>The multiplier for durations and distances at <paramref name="element"/>: 0 off, 0.6 subtle, 1 normal, 1.4 lively.</summary>
        /// <param name="element">Any element of a Veneer tree (it looks up the tree for the motion class; an element outside a tree is normal).</param>
        public static float Scale(VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.ClassListContains(OffClass))
                {
                    return 0f;
                }

                if (current.ClassListContains(SubtleClass))
                {
                    return 0.6f;
                }

                if (current.ClassListContains(LivelyClass))
                {
                    return 1.4f;
                }

                if (current.ClassListContains("vn-root"))
                {
                    break;
                }
            }

            return 1f;
        }

        /// <summary>True when motion is switched off at <paramref name="element"/>.</summary>
        public static bool IsOff(VisualElement element)
        {
            return Scale(element) <= 0f;
        }
    }
}
