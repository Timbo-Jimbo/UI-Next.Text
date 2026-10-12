using TimboJimbo.UINext.Text;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UINext.Text
{
    /// <summary>Shows only the fields that apply to the trigger's kind, each named for what it is there.</summary>
    [CustomPropertyDrawer(typeof(TextTrigger))]
    public sealed class TextTriggerDrawer : PropertyDrawer
    {
        private readonly struct Field
        {
            public readonly string Name;
            public readonly GUIContent Label;

            public Field(string name, string label, string tooltip)
            {
                Name = name;
                Label = new GUIContent(label, tooltip);
            }
        }

        private static readonly Field[] s_wrapped =
        {
            new("Open", "Open", "The opening delimiter: ** for bold, ` for code."),
            new("Close", "Close", "The closing delimiter; empty for the opening one again."),
            new("InsideWords", "Inside Words", "Its delimiters may stand inside a word, as ** does in un**believ**able. Off, they stand at a word's edges, so snake_case stays text."),
            new("Literal", "Literal", "Its text is taken as written, with no rule matching inside it, as code is. A delimiter of one character repeated may then be written longer to wrap text holding it: ``a`b``."),
        };

        private static readonly Field[] s_prefix =
        {
            new("Open", "Prefix", "What comes before the word: @ for a mention, # for a tag. Both are shown; its text is the word."),
        };

        private static readonly Field[] s_line =
        {
            new("Open", "Marker", "What the line starts with, after any spaces: \"# \" for a heading, \"> \" for a quote."),
            new("Pattern", "Marker Pattern", "A regular expression for the marker, in place of Marker: \\d+\\. for numbered items."),
            new("Merge", "Merge", "A run of lines with the marker is one match, as the lines of a quote are."),
            new("Literal", "Literal", "Its text is taken as written, with no rule matching inside it."),
        };

        private static readonly Field[] s_fenced =
        {
            new("Open", "Fence", "The line that opens and closes it: ```. What follows the opening fence (a language) is its argument."),
            new("Literal", "Literal", "Its text is taken as written, with no rule matching inside it, as code is."),
        };

        private static readonly Field[] s_link =
        {
            new("BareUrls", "Bare URLs", "Web addresses written as they are (https://…) too, as well as [text](target)."),
        };

        private static readonly Field[] s_pattern =
        {
            new("Pattern", "Pattern", "A .NET regular expression, within a line. A group named content is its text, shown when it stays text, and one named arg its argument."),
            new("Literal", "Literal", "Its text is taken as written, with no rule matching inside it."),
        };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            return line * (1 + Fields(property).Length) - EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            float step = line + EditorGUIUtility.standardVerticalSpacing;
            var rect = new Rect(position.x, position.y, position.width, line);
            EditorGUI.PropertyField(rect, property.FindPropertyRelative(nameof(TextTrigger.Kind)), label);
            EditorGUI.indentLevel++;
            foreach (var field in Fields(property))
            {
                rect.y += step;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative(field.Name), field.Label);
            }
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static Field[] Fields(SerializedProperty property) =>
            (TextTriggerKind)property.FindPropertyRelative(nameof(TextTrigger.Kind)).enumValueIndex switch
            {
                TextTriggerKind.Prefix => s_prefix,
                TextTriggerKind.Line => s_line,
                TextTriggerKind.Fenced => s_fenced,
                TextTriggerKind.Link => s_link,
                TextTriggerKind.Pattern => s_pattern,
                _ => s_wrapped,
            };
    }
}
