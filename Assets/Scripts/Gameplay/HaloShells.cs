using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the "glow halo" used by GrabHighlight and AimGlow: one slightly
/// enlarged copy of every mesh under a root, all sharing one additive
/// material. Padding is in metres (not a scale multiplier) so a thin bone
/// gets as visible a halo as a fat log.
/// </summary>
public static class HaloShells
{
    public const string ShellName = "Grab Glow Shell";

    public static Renderer[] Build(Transform root, Material material, float thickness)
    {
        var shells = new List<Renderer>();

        foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.GetComponent<Renderer>() == null || filter.name == ShellName)
                continue;

            var shell = new GameObject(ShellName);
            shell.transform.SetParent(filter.transform, false);

            Vector3 lossy = filter.transform.lossyScale;
            Vector3 size = filter.sharedMesh.bounds.size;
            var scale = Vector3.one;
            for (int axis = 0; axis < 3; axis++)
            {
                float world = Mathf.Abs(size[axis] * lossy[axis]);
                scale[axis] = 1f + Mathf.Min(2f, 2f * thickness / Mathf.Max(0.005f, world));
            }
            shell.transform.localScale = scale;

            shell.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = shell.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            shells.Add(renderer);
        }

        return shells.ToArray();
    }
}
