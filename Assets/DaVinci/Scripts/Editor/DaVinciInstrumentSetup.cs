using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace XRMultiplayer.DaVinci.Editor
{
    /// <summary>
    /// Turns the loose tool meshes into grabbable instruments and fits the arm tip with a coupling
    /// they seat into.
    /// </summary>
    /// <remarks>
    /// Every placement here is measured off the meshes rather than typed in, because the seat point
    /// depends on how each tool was modelled and its pivot. Reading it from the renderer bounds
    /// gives a sensible starting point for any of them, and re-running after a model change picks
    /// up the new proportions.
    /// </remarks>
    public static class DaVinciInstrumentSetup
    {
        const string k_Title = "Da Vinci Instrument Setup";
        const string k_MountName = "Instrument Mount";
        const string k_DefaultForceps = "FORCEPS_1.002";
        const string k_ToolGroup = "Swappable Tools";

        static readonly string[] k_ToolNames = { "Tweezers", "Clamps", "Scalpel" };

        /// <summary>Capture radius of the coupling, in world metres.</summary>
        const float k_SocketRadius = 0.06f;

        [MenuItem("Tools/Da Vinci/Set Up Instrument Swapping", false, 12)]
        [MenuItem("GameObject/Da Vinci/Set Up Instrument Swapping", false, 15)]
        public static void Setup()
        {
            var notes = new List<string>();

            var forceps = ResolveForceps();
            if (forceps == null)
            {
                EditorUtility.DisplayDialog(k_Title,
                    $"Could not find \"{k_DefaultForceps}\" in the open scene.\n\n" +
                    "Select the arm tip you want the coupling on and run this again.", "OK");
                return;
            }

            RemoveStrayMounts(forceps, notes);

            var mount = BuildMount(forceps, notes);

            var tools = ResolveTools(notes);
            if (tools.Count == 0)
            {
                EditorUtility.DisplayDialog(k_Title,
                    $"Found the arm tip but no tools. Looked for a \"{k_ToolGroup}\" object, then for: " +
                    string.Join(", ", k_ToolNames) + ".", "OK");
                return;
            }

            foreach (var tool in tools)
                BuildInstrument(tool, notes);

            EditorSceneManager.MarkSceneDirty(forceps.gameObject.scene);
            Debug.Log($"[{k_Title}] Done.\n  " + string.Join("\n  ", notes), mount);
        }

        /// <summary>
        /// Finds the arm tip to fit. A selection only counts as an override when it actually looks
        /// like a forceps.
        /// </summary>
        /// <remarks>
        /// An earlier version took whatever happened to be selected, which fitted couplings to
        /// whatever the user last clicked — a floor tile, and one of the tools. A socket on a tool
        /// then grabbed its neighbour off the tray and hid the tool it was sitting on, which looked
        /// like the tools vanishing.
        /// </remarks>
        /// <summary>
        /// Turns a tool's seat point half a turn about its own shaft, for one that seats the right
        /// way round but upside down.
        /// </summary>
        /// <remarks>
        /// Which end of a tool is the handle is the one thing the setup cannot measure — the seat
        /// point is placed by a heuristic — so this is the correction for when the heuristic lands
        /// the wrong way up. Nothing else needs touching: the socket aligns to this transform, so
        /// turning it turns how the tool sits. Any other correction can be dialled straight into the
        /// Mount Point child's rotation in the Inspector.
        /// </remarks>
        [MenuItem("Tools/Da Vinci/Flip Instrument Mount", false, 13)]
        public static void FlipInstrumentMount()
        {
            var flipped = 0;

            foreach (var go in Selection.gameObjects)
            {
                var instrument = go.GetComponentInParent<DaVinciInstrument>();
                if (instrument == null)
                    continue;

                var mount = instrument.mountPoint;
                if (mount == null || mount == instrument.transform)
                    continue;

                Undo.RecordObject(mount, "Flip Instrument Mount");

                // About the shaft axis, which is the seat point's own forward.
                mount.rotation = Quaternion.AngleAxis(180f, mount.forward) * mount.rotation;
                EditorUtility.SetDirty(mount);
                flipped++;
            }

            if (flipped == 0)
            {
                EditorUtility.DisplayDialog(k_Title,
                    "Select one or more tools that have already been set up as instruments.", "OK");
                return;
            }

            Debug.Log($"[{k_Title}] Turned {flipped} seat point(s) half a turn about the shaft.");
        }

        static Transform ResolveForceps()
        {
            var selected = Selection.activeTransform;
            if (selected != null && selected.name.StartsWith("FORCEPS"))
                return selected;

            return FindInScene(k_DefaultForceps);
        }

        /// <summary>
        /// Removes couplings that were fitted somewhere they do not belong, restoring any renderer
        /// they had switched off.
        /// </summary>
        static void RemoveStrayMounts(Transform keep, List<string> notes)
        {
            foreach (var mount in Object.FindObjectsByType<DaVinciInstrumentMount>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mount == null || mount.transform.parent == keep)
                    continue;

                var host = mount.transform.parent != null ? mount.transform.parent.name : "<scene root>";

                // Put back whatever it was hiding before it goes, or the object stays invisible
                // with nothing left to explain why.
                foreach (var renderer in mount.GetComponentsInParent<Renderer>(true))
                    renderer.enabled = true;

                Undo.DestroyObjectImmediate(mount.gameObject);
                notes.Add($"Removed a stray coupling that had been fitted to \"{host}\", and re-enabled its renderer.");
            }
        }

        /// <summary>
        /// Collects the tools, preferring an explicit group so more can be added without touching
        /// this list.
        /// </summary>
        static List<Transform> ResolveTools(List<string> notes)
        {
            var tools = new List<Transform>();

            var group = FindInScene(k_ToolGroup);
            if (group != null)
            {
                foreach (Transform child in group)
                {
                    if (child.GetComponentInChildren<Renderer>(true) != null)
                        tools.Add(child);
                }

                notes.Add($"Took {tools.Count} tool(s) from \"{k_ToolGroup}\".");
                return tools;
            }

            foreach (var name in k_ToolNames)
            {
                var found = FindInScene(name);
                if (found != null)
                    tools.Add(found);
            }

            notes.Add($"No \"{k_ToolGroup}\" object found, so fell back to finding {tools.Count} tool(s) by name. " +
                      "Group them under an object of that name to control the set.");
            return tools;
        }

        static DaVinciInstrumentMount BuildMount(Transform forceps, List<string> notes)
        {
            var existing = forceps.Find(k_MountName);
            GameObject go;

            if (existing != null)
            {
                go = existing.gameObject;
                notes.Add("Reused the existing coupling.");
            }
            else
            {
                go = new GameObject(k_MountName);
                Undo.RegisterCreatedObjectUndo(go, "Set Up Instrument Swapping");
                Undo.SetTransformParent(go.transform, forceps, "Set Up Instrument Swapping");
                notes.Add($"Fitted a coupling to \"{forceps.name}\".");
            }

            // Seat the coupling at the far end of the forceps, facing on down the shaft, so a tool
            // fitted here continues the instrument rather than growing back out of the arm.
            var shaft = ShaftDirection(forceps);
            var bounds = WorldBounds(forceps);
            go.transform.SetPositionAndRotation(
                bounds.center + shaft * bounds.extents.magnitude,
                Quaternion.LookRotation(shaft, Vector3.up));
            go.transform.localScale = Vector3.one;

            var trigger = go.GetComponent<SphereCollider>();
            if (trigger == null)
                trigger = Undo.AddComponent<SphereCollider>(go);

            trigger.isTrigger = true;

            // The arm model is imported at a fraction of a unit, and a child inherits that. Without
            // dividing it out, a radius meant as six centimetres would capture at barely one.
            var scale = go.transform.lossyScale;
            var uniform = Mathf.Max(0.0001f, Mathf.Abs(Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z))));
            trigger.radius = k_SocketRadius / uniform;

            var mount = go.GetComponent<DaVinciInstrumentMount>();
            if (mount == null)
                mount = Undo.AddComponent<DaVinciInstrumentMount>(go);

            mount.SetDefaultInstrument(forceps.GetComponent<Renderer>());

            // The socket aligns a tool's attach transform with this one, so it has to be the seat
            // point itself rather than the socket object's own pivot.
            mount.attachTransform = go.transform;

            EditorUtility.SetDirty(go);
            return mount;
        }

        static void BuildInstrument(Transform tool, List<string> notes)
        {
            Undo.RegisterFullObjectHierarchyUndo(tool.gameObject, "Set Up Instrument Swapping");

            // A non-convex mesh cannot act as a moving collider, and these import that way. Convex
            // is also what the socket's trigger needs to register an overlap reliably.
            foreach (var mesh in tool.GetComponentsInChildren<MeshCollider>(true))
            {
                if (!mesh.convex)
                {
                    mesh.convex = true;
                    notes.Add($"Made \"{mesh.gameObject.name}\" collider convex so it can be carried.");
                }
            }

            if (tool.GetComponentInChildren<Collider>(true) == null)
            {
                var box = Undo.AddComponent<BoxCollider>(tool.gameObject);
                var local = LocalBounds(tool);
                box.center = local.center;
                box.size = local.size;
                notes.Add($"\"{tool.name}\" had no collider, so a box was fitted.");
            }

            var body = tool.GetComponent<Rigidbody>();
            if (body == null)
                body = Undo.AddComponent<Rigidbody>(tool.gameObject);

            // Given weight, so a tool that misses the coupling falls and lands instead of hanging
            // in mid-air. That is also the cue that a drop failed: a tool left floating reads as a
            // bug, a tool on the floor reads as a dropped instrument.
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var grab = tool.GetComponent<XRGrabInteractable>();
            if (grab == null)
                grab = Undo.AddComponent<XRGrabInteractable>(tool.gameObject);

            // Kinematic movement suspends physics only for as long as the tool is held, and XRI
            // restores the body's own settings on release, so gravity resumes the moment it is let
            // go. Instantaneous would keep writing the transform out from under the simulation.
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.throwOnDetach = false;
            grab.retainTransformParent = true;

            var instrument = tool.GetComponent<DaVinciInstrument>();
            if (instrument == null)
                instrument = Undo.AddComponent<DaVinciInstrument>(tool.gameObject);

            instrument.SetMountPoint(BuildMountPoint(tool));
            EditorUtility.SetDirty(tool.gameObject);
            notes.Add($"\"{tool.name}\" is now a grabbable instrument.");
        }

        /// <summary>
        /// Places the seat point at the end of the tool nearest its pivot, facing on down its
        /// longest axis toward the working end.
        /// </summary>
        static Transform BuildMountPoint(Transform tool)
        {
            var existing = tool.Find("Mount Point");
            if (existing != null)
                return existing;

            var go = new GameObject("Mount Point");
            Undo.RegisterCreatedObjectUndo(go, "Set Up Instrument Swapping");
            Undo.SetTransformParent(go.transform, tool, "Set Up Instrument Swapping");

            var bounds = WorldBounds(tool);
            var axis = LongestAxis(bounds);
            var half = axis * bounds.extents.magnitude;

            // Of the two ends, the one closer to the pivot is the handle on most props, which is
            // the end that goes into the arm.
            var nearEnd = bounds.center - half;
            var farEnd = bounds.center + half;
            if (Vector3.Distance(farEnd, tool.position) < Vector3.Distance(nearEnd, tool.position))
                (nearEnd, farEnd) = (farEnd, nearEnd);

            var forward = (farEnd - nearEnd).normalized;
            go.transform.SetPositionAndRotation(nearEnd, Quaternion.LookRotation(forward, Vector3.up));
            go.transform.localScale = Vector3.one;

            return go.transform;
        }

        static Vector3 ShaftDirection(Transform tip)
        {
            if (tip.parent == null)
                return tip.forward;

            var shaft = tip.position - tip.parent.position;
            return shaft.sqrMagnitude > 1e-8f ? shaft.normalized : tip.forward;
        }

        static Vector3 LongestAxis(Bounds bounds)
        {
            var size = bounds.size;
            if (size.x >= size.y && size.x >= size.z)
                return Vector3.right;

            return size.y >= size.z ? Vector3.up : Vector3.forward;
        }

        static Bounds WorldBounds(Transform target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(target.position, Vector3.one * 0.05f);

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        static Bounds LocalBounds(Transform target)
        {
            var world = WorldBounds(target);
            return new Bounds(target.InverseTransformPoint(world.center), target.InverseTransformVector(world.size));
        }

        static Transform FindInScene(string name)
        {
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name == name)
                        return candidate;
                }
            }

            return null;
        }
    }
}
