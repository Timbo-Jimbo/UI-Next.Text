using System.Collections.Generic;
using TimboJimbo.UINext.Text;
using UnityEditor;
using UnityEngine;

namespace TimboJimboEditor.UINext.Text
{
    /// <summary>
    /// Edit > Project Settings > Timbo Jimbo > UI Next Text: picks the active <see cref="TextSettings"/> asset and
    /// edits its fields in place. The choice is an EditorBuildSettings config object, so it lives in
    /// ProjectSettings and <see cref="PreloadTextDependenciesInBuild"/> can preload the asset into builds.
    /// </summary>
    internal static class TextSettingsProvider
    {
        /// <summary>The registered settings asset, or null. Setting it registers the asset and applies it to open texts.</summary>
        internal static TextSettings Active
        {
            get
            {
                EditorBuildSettings.TryGetConfigObject(TextSettings.ConfigObjectKey, out TextSettings settings);
                return settings;
            }
            set
            {
                if (value != null)
                    EditorBuildSettings.AddConfigObject(TextSettings.ConfigObjectKey, value, true);
                else
                    EditorBuildSettings.RemoveConfigObject(TextSettings.ConfigObjectKey);
                TextSettings.SetActive(value);
            }
        }

        // The first time a project opens with no settings registered, the first settings asset in it is registered, so
        // texts find their default font without a visit to this page.
        [InitializeOnLoadMethod]
        private static void RegisterFound() => EditorApplication.delayCall += () =>
        {
            if (Active != null) return;
            var guids = AssetDatabase.FindAssets("t:" + nameof(TextSettings));
            if (guids.Length == 0) return;
            var found = AssetDatabase.LoadAssetAtPath<TextSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (found != null)
                Active = found;
        };

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            SerializedObject serializedObject = null;

            return new SettingsProvider("Project/Timbo Jimbo/UI Next Text", SettingsScope.Project)
            {
                label = "UI Next Text",
                keywords = new HashSet<string>(new[] { "Text", "Font", "Markup", "Markdown" }),
                deactivateHandler = () => serializedObject = null,
                guiHandler = _ =>
                {
                    var previousLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 250;

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(10);
                    EditorGUILayout.BeginVertical();
                    GUILayout.Space(10);

                    var active = Active;
                    var picked = (TextSettings)EditorGUILayout.ObjectField("Settings Asset", active, typeof(TextSettings), false);
                    if (picked != active)
                        Active = active = picked;

                    if (active == null)
                    {
                        EditorGUILayout.HelpBox("No settings asset. Texts use the OS default font and the built-in Markdown.", MessageType.Info);
                        if (GUILayout.Button("Create", GUILayout.Width(120)))
                        {
                            var created = CreateAsset();
                            if (created != null)
                                Active = created;
                        }
                    }
                    else
                    {
                        if (serializedObject == null || serializedObject.targetObject != active)
                            serializedObject = new SerializedObject(active);
                        serializedObject.Update();
                        EditorGUILayout.Space();
                        var property = serializedObject.GetIterator();
                        for (bool enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
                            if (property.propertyPath != "m_Script")
                                EditorGUILayout.PropertyField(property, true);
                        // Applying runs the asset's OnValidate, which redraws every text.
                        serializedObject.ApplyModifiedProperties();
                    }

                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();

                    EditorGUIUtility.labelWidth = previousLabelWidth;
                }
            };
        }

        private static TextSettings CreateAsset()
        {
            var path = EditorUtility.SaveFilePanelInProject("Create Text Block Settings", "TextSettings", "asset", "Where to save the settings asset.");
            if (string.IsNullOrEmpty(path))
                return null;
            var asset = ScriptableObject.CreateInstance<TextSettings>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
