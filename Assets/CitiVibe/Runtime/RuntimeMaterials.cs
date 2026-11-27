using System.Collections.Generic;
using UnityEngine;

namespace CitiVibe
{
    /// <summary>In the Editor, play-mode copies of shared material assets so runtime changes never write back to the project.</summary>
    public static class RuntimeMaterials
    {
        static readonly Dictionary<Material, Material> clones = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => clones.Clear();

        /// <summary>Returns the runtime copy of <paramref name="asset"/>, swapped into every renderer that uses it.</summary>
        public static Material Instance(Material asset)
        {
            if (asset == null) return null;
#if !UNITY_EDITOR
            // Players can't write back to project assets, and statically batched renderers keep their
            // build-time materials, so edit the shared material directly.
            return asset;
#else
            if (clones.TryGetValue(asset, out var copy) && copy != null) return copy;
            copy = new Material(asset) { name = asset.name + " (Runtime)" };
            clones[asset] = copy;
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != asset) continue;
                    mats[i] = copy;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = mats;
            }
            return copy;
#endif
        }
    }
}
