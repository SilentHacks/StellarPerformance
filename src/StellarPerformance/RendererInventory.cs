using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ships.Visuals;
using UnityEngine;

namespace StellarPerformance;

// One inventory at capture start, outside the timed interval. Renderer counts are NOT
// draw calls: passes, shadows, Canvas rendering and indirect GPU draws differ.
internal static class RendererInventory
{
    internal static string Capture()
    {
        var renderers = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        var shaders = new Dictionary<string, int>();
        var meshes = new Dictionary<Mesh, int>();
        var materials = new HashSet<Material>();
        var shipParts = 0; var visible = 0; var readable = 0; var stock = 0; var blocks = 0;
        foreach (var renderer in renderers)
        {
            if (!renderer.enabled || renderer.forceRenderingOff) continue;
            if (renderer.isVisible) visible++;
            if (renderer.GetComponentInParent<ShipPartVisuals>() != null) shipParts++;
            if (renderer.HasPropertyBlock()) blocks++;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var mesh = filter.sharedMesh;
                if (mesh.isReadable) readable++;
                meshes.TryGetValue(mesh, out var count); meshes[mesh] = count + 1;
            }
            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null) continue;
                materials.Add(material);
                var name = material.shader.name;
                if (ShipPartBatching.SupportedShader(name)) stock++;
                shaders.TryGetValue(name, out var count); shaders[name] = count + 1;
            }
        }
        var text = new StringBuilder();
        text.Append("One-shot renderer inventory (outside capture timing; renderer counts are not draw calls): active mesh renderers=").Append(renderers.Length)
            .Append(", recently visible enabled=").Append(visible).Append(", enabled under ShipPartVisuals=").Append(shipParts)
            .Append(", readable meshes=").Append(readable).Append(", unique mesh assets=").Append(meshes.Count).Append(", unique material instances=").Append(materials.Count)
            .Append(", stock URP material slots=").Append(stock).Append(", property-block renderers=").Append(blocks).Append('\n');
        foreach (var pair in shaders.OrderByDescending(p => p.Value).Take(12)) text.Append("Renderer shader[").Append(pair.Key).Append("]: material slots=").Append(pair.Value).Append('\n');
        text.Append("Repeated mesh renderer references (largest 12 groups): ").Append(string.Join(",", meshes.Values.OrderByDescending(v => v).Take(12)));
        return text.ToString();
    }
}
