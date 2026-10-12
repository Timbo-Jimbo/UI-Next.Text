using TimboJimbo.UINext.Text;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UINext.Text
{
    /// <summary>Shows only the fields that apply to the source's kind.</summary>
    [CustomPropertyDrawer(typeof(FontSource))]
    public sealed class FontSourceDrawer : PropertyDrawer
    {
        private static readonly string[] s_osFields = { "Family", "SyntheticBold" };
        private static readonly string[] s_fileFields = { "File", "BoldFile", "ItalicFile", "BoldItalicFile", "SyntheticBold" };
        private static readonly string[] s_assetFields = { "Asset" };

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float line = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            return line * (1 + Fields(property).Length);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            float line = EditorGUIUtility.singleLineHeight;
            float step = line + EditorGUIUtility.standardVerticalSpacing;
            var rect = new Rect(position.x, position.y, position.width, line);
            EditorGUI.PropertyField(rect, property.FindPropertyRelative("Kind"), label);
            EditorGUI.indentLevel++;
            foreach (var field in Fields(property))
            {
                rect.y += step;
                EditorGUI.PropertyField(rect, property.FindPropertyRelative(field));
            }
            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        private static string[] Fields(SerializedProperty property) => (FontSourceKind)property.FindPropertyRelative("Kind").enumValueIndex switch
        {
            FontSourceKind.FontFile => s_fileFields,
            FontSourceKind.FontAsset => s_assetFields,
            _ => s_osFields,
        };
    }
}
