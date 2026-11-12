namespace Veneer
{
    /// <summary>
    /// Text rules of the kit that are plain C# (no Unity calls), so they can be checked outside the editor. Used while Inspectors are
    /// built, never per frame.
    /// </summary>
    public static class VeneerText
    {
        /// <summary>
        /// Raises the first letter of every word of a field label: <c>Starts at</c> becomes <c>Starts At</c>, <c>fwd-left</c> becomes
        /// <c>Fwd-Left</c>, <c>poise bonus (super armor)</c> becomes <c>Poise Bonus (Super Armor)</c>. A word starts after a space, a hyphen
        /// or an opening parenthesis (or at the start). Only a lower-case first letter is changed: the rest of a word is never touched, so
        /// <c>NPCs</c>, <c>HDR</c> and <c>UV</c> stay as they are, and a word that starts with a digit or a symbol is left alone. Spaces and
        /// punctuation are kept. Use it for the label of a field row only (not for headings, notes, tooltips or buttons).
        /// </summary>
        /// <param name="text">The label; null or empty is returned as it is.</param>
        /// <returns>The same string instance when nothing needs to change; otherwise a new string.</returns>
        public static string TitleCase(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            // First pass: find the first letter to raise, so a label that is already right costs no allocation.
            int first = -1;
            for (int i = 0; i < text.Length; i++)
            {
                if (NeedsRaise(text, i))
                {
                    first = i;
                    break;
                }
            }

            if (first < 0)
            {
                return text;
            }

            char[] letters = text.ToCharArray();
            for (int i = first; i < letters.Length; i++)
            {
                if (NeedsRaise(text, i))
                {
                    letters[i] = char.ToUpperInvariant(letters[i]);
                }
            }

            return new string(letters);
        }

        // A lower-case letter that starts a word.
        private static bool NeedsRaise(string text, int index)
        {
            if (!char.IsLower(text[index]))
            {
                return false;
            }

            if (index == 0)
            {
                return true;
            }

            char before = text[index - 1];
            return before == ' ' || before == '-' || before == '(';
        }
    }
}
