using System.Collections.Generic;
using UnityEngine;

namespace Veneer
{
    /// <summary>The shape choices of the sample's segmented control with icons (only their look matters).</summary>
    internal enum VeneerThemeSampleShape
    {
        /// <summary>First choice.</summary>
        Sphere,
        /// <summary>Second choice.</summary>
        Box,
        /// <summary>Third choice.</summary>
        Capsule
    }

    /// <summary>The choices of the sample's dropdown (four, so the open list has some length).</summary>
    internal enum VeneerThemeSampleCollider
    {
        /// <summary>First choice.</summary>
        Sphere,
        /// <summary>Second choice.</summary>
        Box,
        /// <summary>Third choice.</summary>
        Capsule,
        /// <summary>Fourth choice.</summary>
        Mesh
    }

    /// <summary>The choices of the sample's segmented control with text labels.</summary>
    internal enum VeneerThemeSampleMode
    {
        /// <summary>First choice.</summary>
        Auto,
        /// <summary>Second choice.</summary>
        Manual,
        /// <summary>Third choice.</summary>
        Off
    }

    /// <summary>
    /// A throw-away data object that the Theme Editor's live sample binds its controls to (they need a SerializedProperty). It is created
    /// hidden, never saved, and destroyed when the window closes.
    /// </summary>
    internal sealed class VeneerThemeSample : ScriptableObject
    {
        /// <summary>A number with a minimum.</summary>
        [Tooltip("A number with a unit.")]
        [Min(0f)] public float Size = 1.5f;

        /// <summary>A slider value.</summary>
        [Tooltip("A slider with a fill and two end captions.")]
        [Range(0f, 1f)] public float Bounce = 0.4f;

        /// <summary>A slider value with a unit.</summary>
        [Tooltip("A slider with a unit.")]
        [Range(0.1f, 20f)] public float Mass = 4.5f;

        /// <summary>A lowest and a highest value.</summary>
        [Tooltip("A slider with two handles.")]
        public Vector2 Angle = new Vector2(20f, 70f);

        /// <summary>A whole number.</summary>
        [Min(0)] public int Count = 12;

        /// <summary>A toggle that is on.</summary>
        public bool UseGravity = true;

        /// <summary>A toggle that is off.</summary>
        public bool Locked;

        /// <summary>An enum shown as segmented icons.</summary>
        public VeneerThemeSampleShape Shape = VeneerThemeSampleShape.Box;

        /// <summary>An enum shown as a dropdown.</summary>
        [Tooltip("An enum shown as a dropdown.")]
        public VeneerThemeSampleCollider ColliderType = VeneerThemeSampleCollider.Capsule;

        /// <summary>An enum shown as segmented text buttons.</summary>
        [Tooltip("An enum shown as segmented text buttons.")]
        public VeneerThemeSampleMode Mode = VeneerThemeSampleMode.Auto;

        /// <summary>An object slot.</summary>
        public Texture2D Picture;

        /// <summary>A short list.</summary>
        public List<string> Items = new List<string> { "Slash", "Thrust", "Block" };
    }
}
