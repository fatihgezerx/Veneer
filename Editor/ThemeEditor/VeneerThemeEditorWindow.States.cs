using UnityEngine;
using UnityEngine.UIElements;

namespace Veneer
{
    // The "States" card of the live sample: each control drawn in its states (normal, hover, pressed, disabled; a chip also off, on and
    // mixed; a text field with focus) next to each other, every one labelled. A disabled look is the real one (SetEnabled(false)); USS
    // cannot force :hover, :active or :focus, so those three are copies of the kit's own rules under the classes vnte-fake-hover,
    // vnte-fake-pressed and vnte-fake-focus (ThemeEditor.uss), built from the same --vn-* variables so they follow the theme.
    public sealed partial class VeneerThemeEditorWindow
    {
        private const string FakeHoverClass = "vnte-fake-hover";
        private const string FakePressedClass = "vnte-fake-pressed";
        private const string FakeFocusClass = "vnte-fake-focus";

        private static readonly string[] SampleDropdownChoices = { "Sphere", "Box", "Capsule", "Mesh" };

        private VisualElement BuildStatesCard(VeneerBrand brand)
        {
            VisualElement card = VeneerStyle.Card("States", "eye", brand);
            Label note = VeneerStyle.MutedLabel("Every control in its states. Hover and pressed are drawn by hand here; the real ones are in the other tabs.");
            note.AddToClassList("vnte-states__note");
            card.Add(note);

            // Button
            card.Add(StatesTitle("Button"));
            VisualElement row = StatesRow();
            row.Add(State("Normal", NewStateButton("Button", false, brand)));
            row.Add(State("Hover", Fake(NewStateButton("Button", false, brand), FakeHoverClass)));
            row.Add(State("Pressed", Fake(NewStateButton("Button", false, brand), FakePressedClass)));
            row.Add(State("Disabled", Disabled(NewStateButton("Button", false, brand))));
            card.Add(row);

            // Primary button
            card.Add(StatesTitle("Primary button"));
            row = StatesRow();
            row.Add(State("Normal", NewStateButton("Primary", true, brand)));
            row.Add(State("Hover", Fake(NewStateButton("Primary", true, brand), FakeHoverClass)));
            row.Add(State("Pressed", Fake(NewStateButton("Primary", true, brand), FakePressedClass)));
            row.Add(State("Disabled", Disabled(NewStateButton("Primary", true, brand))));
            card.Add(row);

            // Icon button
            card.Add(StatesTitle("Icon button"));
            row = StatesRow();
            row.Add(State("Normal", VeneerStyle.IconButton("settings", "An icon button.", null, brand)));
            row.Add(State("Hover", Fake(VeneerStyle.IconButton("settings", "An icon button, hover look.", null, brand), FakeHoverClass)));
            row.Add(State("Pressed", Fake(VeneerStyle.IconButton("settings", "An icon button, pressed look.", null, brand), FakePressedClass)));
            row.Add(State("Disabled", Disabled(VeneerStyle.IconButton("settings", "An icon button, disabled.", null, brand))));
            card.Add(row);

            // Chip
            card.Add(StatesTitle("Chip"));
            row = StatesRow();
            row.Add(State("Off", VeneerStyle.Chip("Chip", null, false, null, brand)));
            row.Add(State("On", VeneerStyle.Chip("Chip", null, true, null, brand)));
            VisualElement mixed = VeneerStyle.Chip("Chip", null, true, null, brand);
            VeneerStyle.SetChipState(mixed, true, true);
            row.Add(State("Mixed", mixed));
            row.Add(State("Hover", Fake(VeneerStyle.Chip("Chip", null, false, null, brand), FakeHoverClass)));
            row.Add(State("Pressed", Fake(VeneerStyle.Chip("Chip", null, false, null, brand), FakePressedClass)));
            row.Add(State("Disabled", Disabled(VeneerStyle.Chip("Chip", null, false, null, brand))));
            card.Add(row);

            // Dropdown
            card.Add(StatesTitle("Dropdown"));
            row = StatesRow();
            row.Add(State("Normal", WideState(VeneerDropdown.Create(SampleDropdownChoices, 1, null, brand))));
            row.Add(State("Hover", WideState(Fake(VeneerDropdown.Create(SampleDropdownChoices, 1, null, brand), FakeHoverClass))));
            row.Add(State("Disabled", WideState(Disabled(VeneerDropdown.Create(SampleDropdownChoices, 1, null, brand)))));
            card.Add(row);

            // Text field
            card.Add(StatesTitle("Text field"));
            row = StatesRow();
            row.Add(State("Normal", WideState(NewStateField(false))));
            row.Add(State("Focus", WideState(NewStateField(true))));
            row.Add(State("Disabled", WideState(Disabled(NewStateField(false)))));
            card.Add(row);

            // Pills
            card.Add(StatesTitle("Pill"));
            VisualElement pills = VeneerStyle.Row(true);
            pills.Add(VeneerStyle.Pill("Info", "info"));
            pills.Add(VeneerStyle.Pill("Ok", "ok"));
            pills.Add(VeneerStyle.Pill("Idle", "idle"));
            pills.Add(VeneerStyle.Pill("Warn", "warn"));
            pills.Add(VeneerStyle.Pill("Error", "error"));
            pills.Add(VeneerStyle.Pill("Special", "special"));
            card.Add(pills);
            return card;
        }

        private static Label StatesTitle(string text)
        {
            Label title = VeneerStyle.MutedLabel(text);
            title.AddToClassList("vnte-states__title");
            return title;
        }

        // A wrapping line of states; each state is a control with its caption under it.
        private static VisualElement StatesRow()
        {
            var row = new VisualElement();
            row.AddToClassList("vnte-states");
            return row;
        }

        private static VisualElement State(string caption, VisualElement control)
        {
            var state = new VisualElement();
            state.AddToClassList("vnte-state");
            state.Add(control);
            Label label = VeneerStyle.MutedLabel(caption);
            label.AddToClassList("vnte-state__caption");
            state.Add(label);
            return state;
        }

        private static VisualElement Fake(VisualElement element, string fakeClass)
        {
            element.AddToClassList(fakeClass);
            return element;
        }

        private static VisualElement Disabled(VisualElement element)
        {
            element.SetEnabled(false);
            return element;
        }

        // A control with a fixed width (a dropdown or a text field has none of its own inside a column that is only as wide as its content).
        private static VisualElement WideState(VisualElement element)
        {
            element.AddToClassList("vnte-state__wide");
            return element;
        }

        private static Button NewStateButton(string text, bool primary, VeneerBrand brand)
        {
            return VeneerStyle.Button(text, null, primary ? "A primary button." : "A normal button.", null, primary, brand);
        }

        // A text field with some text; the focus look is the kit's own border colour put on the input by hand.
        private static TextField NewStateField(bool focused)
        {
            var field = new TextField();
            field.SetValueWithoutNotify("Text");
            if (focused)
            {
                VisualElement input = field.Q(className: "unity-base-text-field__input");
                if (input != null)
                {
                    input.AddToClassList(FakeFocusClass);
                }
            }

            return field;
        }
    }
}
