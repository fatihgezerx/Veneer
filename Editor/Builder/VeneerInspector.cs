using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    /// <summary>
    /// Base class of a studio-styled Inspector. Derive from it (or from <see cref="VeneerInspector{T}"/>), override <see cref="Build"/>
    /// and describe the layout with the builder; the kit makes the root (kit sheet, brand, light/dark skin), calls <c>Build</c>, binds
    /// everything and starts the scheduler. If <c>Build</c> throws, the exception is logged and an error message replaces the layout, so
    /// the Inspector never goes blank. Add <c>[CanEditMultipleObjects]</c> to your editor when the type allows it.
    /// </summary>
    [CanEditMultipleObjects]
    public abstract class VeneerInspector : Editor
    {
        /// <summary>The brand of the system (accent, logo, icon folder). Null uses the default look. Return a cached instance.</summary>
        protected virtual VeneerBrand Brand => null;

        /// <summary>Makes the root and calls <see cref="Build"/>.</summary>
        public override VisualElement CreateInspectorGUI()
        {
            VeneerBrand brand = Brand;
            VisualElement root = VeneerStyle.Root(brand);
            var builder = new VeneerInspectorBuilder(serializedObject, root, brand);
            try
            {
                Build(builder);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                root.Add(VeneerStyle.Hint("The Inspector layout failed: " + exception.Message, "error", brand));
            }

            builder.Finish();
            return root;
        }

        /// <summary>Describes the layout. Add content with the builder; do not keep it after this call returns.</summary>
        protected abstract void Build(VeneerInspectorBuilder b);
    }

    /// <summary>
    /// <see cref="VeneerInspector"/> for one target type: <c>[CustomEditor(typeof(Foo))] class FooEditor : VeneerInspector&lt;Foo&gt;</c>.
    /// </summary>
    /// <typeparam name="T">The edited type.</typeparam>
    public abstract class VeneerInspector<T> : VeneerInspector where T : UnityEngine.Object
    {
        /// <summary>The first selected object, typed.</summary>
        protected T Target => (T)target;

        /// <summary>How many objects are selected.</summary>
        protected int TargetCount => targets.Length;
    }
}
