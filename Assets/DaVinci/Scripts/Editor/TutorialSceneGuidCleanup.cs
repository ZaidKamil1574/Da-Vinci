using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Strips the interactive tutorial framework's <c>SceneObjectGuid</c> components out of the
    /// open scenes, which is what makes Player builds fail with a duplicate-key exception.
    /// </summary>
    /// <remarks>
    /// <para>
    /// com.unity.learn.iet-framework registers these components in a static dictionary using
    /// <c>Dictionary.Add</c>, which throws when the key is already present rather than overwriting.
    /// The component's id is serialized but its "already registered" flag is not, so after the
    /// domain reload a build performs, the component believes it is unregistered while the static
    /// dictionary still holds its id. Registering then throws out of <c>OnValidate</c> and takes the
    /// build down with it.
    /// </para>
    /// <para>
    /// These components exist only so tutorial steps can point at scene objects. Removing them from
    /// a project that is no longer running the template's onboarding tutorial costs nothing and
    /// leaves the package, its assets, and TutorialCallbacks.cs compiling as they are.
    /// </para>
    /// <para>
    /// The type is matched by name rather than referenced directly, so this keeps working if the
    /// tutorial package is later removed from the project.
    /// </para>
    /// </remarks>
    public static class TutorialSceneGuidCleanup
    {
        const string k_TypeName = "Unity.Tutorials.Core.SceneObjectGuid";

        [MenuItem("Tools/Da Vinci/Remove Tutorial Scene GUIDs", false, 100)]
        static void RemoveTutorialGuids()
        {
            var found = new List<Component>();

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                    CollectFrom(root, found);
            }

            if (found.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Tutorial Scene GUIDs",
                    "No SceneObjectGuid components found in the open scenes.\n\n" +
                    "If the build still fails, open every scene listed in Build Settings and run this again — " +
                    "the components are hidden, so they are easy to miss.",
                    "OK");
                return;
            }

            var scenesTouched = new HashSet<Scene>();
            foreach (var component in found)
            {
                scenesTouched.Add(component.gameObject.scene);
                Undo.DestroyObjectImmediate(component);
            }

            foreach (var scene in scenesTouched)
                EditorSceneManager.MarkSceneDirty(scene);

            EditorUtility.DisplayDialog(
                "Tutorial Scene GUIDs",
                $"Removed {found.Count} SceneObjectGuid component(s) from {scenesTouched.Count} scene(s).\n\n" +
                "Save the scene, then build again.",
                "OK");
        }

        static void CollectFrom(GameObject target, List<Component> found)
        {
            // GetComponents rather than GetComponentsInChildren<T>: the type is matched by name so
            // that this does not need a reference to the tutorial package's assembly.
            foreach (var component in target.GetComponentsInChildren<Component>(true))
            {
                // A missing script deserialises as null and would throw on GetType().
                if (component == null)
                    continue;

                if (component.GetType().FullName == k_TypeName)
                    found.Add(component);
            }
        }
    }
}
