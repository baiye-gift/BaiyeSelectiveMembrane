using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Baiye.SelectiveMembrane.Core;

namespace Baiye.SelectiveMembrane
{
    // Diagnostic graphics only. Does not rotate buildings, reserve or modify cells.
    public sealed class MembraneFlowPreview : KMonoBehaviour
    {
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        Material material;
        KSelectable selectable;
        Building building;
        Rotatable rotatable;
        MembraneBuilding membrane;
        int used;
        bool warned;
        protected override void OnSpawn()
        {
            base.OnSpawn(); selectable = GetComponent<KSelectable>(); building = GetComponent<Building>();
            rotatable = GetComponent<Rotatable>(); membrane = GetComponent<MembraneBuilding>();
        }
        internal static bool IsMembrane(BuildingDef def) => def != null && (def.PrefabID == MembraneTileConfig.ID || def.PrefabID == MembraneDoorConfig.ID);
        internal static int[] Placement(BuildingDef def, int origin, Orientation orientation)
        {
            var cells = new List<int>();
            def.RunOnArea(origin, orientation, c => cells.Add(c)); return cells.ToArray();
        }
        internal static bool Blocked(int cell, int world, GameObject preview = null)
        {
            if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world) return true;
            if (Grid.Solid[cell] || Grid.Element[cell]?.IsSolid == true) return true;
            // Scheduled foundation tiles occupy this layer before becoming Sim solids.
            var planned = Grid.Objects[cell, (int)ObjectLayer.FoundationTile];
            return planned != null && planned != preview && planned.GetComponent<Constructable>() != null && planned.GetComponent<Building>()?.Def?.BuildingComplete?.GetComponent<SimCellOccupier>() != null;
        }
        void Update()
        {
            bool preview = BuildTool.Instance != null && BuildTool.Instance.visualizer == gameObject && PlayerController.Instance?.ActiveTool == BuildTool.Instance;
            bool selected = selectable != null && selectable.IsSelected;
            used = 0;
            if ((preview || selected) && building != null && IsMembrane(building.Def))
            {
                var orientation = rotatable?.GetOrientation() ?? Orientation.Neutral;
                bool vertical = orientation != Orientation.Neutral;
                int origin = Grid.PosToCell(transform.position);
                if (Grid.IsValidCell(origin) && ClusterManager.Instance != null && Grid.WorldIdx[origin] == ClusterManager.Instance.activeWorldId)
                {
                    int world = Grid.WorldIdx[origin];
                    var cfg = membrane != null && membrane.Ready ? membrane.State.Applied : null;
                    foreach (int cell in Placement(building.Def, origin, orientation))
                    {
                        if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world) continue;
                        var ends = FlowGuidance.Endpoints(cell, vertical, Grid.WidthInCells, Grid.CellCount);
                        Draw(cell, ends.Item1, ends.Item2, vertical, world, cfg);
                    }
                }
            }
            for (int i = used; i < lines.Count; i++) lines[i].gameObject.SetActive(false);
        }
        void Draw(int cell, int a, int b, bool vertical, int world, MembraneSettings cfg)
        {
            bool blockedA = Blocked(a, world, gameObject), blockedB = Blocked(b, world, gameObject);
            Color clear = new Color(.30f, .86f, .94f, .95f), blocked = new Color(1, .55f, .24f, 1);
            Vector3 center = Grid.CellToPosCCC(cell, Grid.SceneLayer.FXFront2);
            Vector3 axis = vertical ? Vector3.up : Vector3.right, normal = vertical ? Vector3.right : Vector3.up;
            Vector3 first = center - axis * .82f, second = center + axis * .82f;
            Color color = blockedA || blockedB ? blocked : clear;
            Line(color, first, second);
            // Preview is unconfigured/passive: both ends. Completed active: one end.
            if (cfg == null || !cfg.Active || cfg.Direction == -1) Line(color, first + axis * .20f + normal * .13f, first, first + axis * .20f - normal * .13f);
            if (cfg == null || !cfg.Active || cfg.Direction == 1) Line(color, second - axis * .20f + normal * .13f, second, second - axis * .20f - normal * .13f);
            Outline(a, world, blockedA ? blocked : clear); Outline(b, world, blockedB ? blocked : clear);
        }
        void Outline(int cell, int world, Color color)
        {
            if (!Grid.IsValidCell(cell) || Grid.WorldIdx[cell] != world) return;
            Vector3 center = Grid.CellToPosCCC(cell, Grid.SceneLayer.FXFront2);
            Line(color, center + new Vector3(-.43f, -.43f), center + new Vector3(-.43f, .43f), center + new Vector3(.43f, .43f), center + new Vector3(.43f, -.43f), center + new Vector3(-.43f, -.43f));
        }
        void Line(Color color, params Vector3[] points)
        {
            if (material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) { if (!warned) Debug.LogWarning("[BaiyeSelectiveMembrane] Preview shader unavailable; native axis text remains available."); warned = true; return; }
                material = new Material(shader);
            }
            if (used == lines.Count)
            {
                var go = new GameObject("MembraneAxisGuide"); go.layer = LayerMask.NameToLayer("Overlay"); go.transform.SetParent(transform, false);
                var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = material; line.useWorldSpace = true;
                line.startWidth = line.endWidth = .045f; line.numCapVertices = 2; lines.Add(line);
            }
            var renderer = lines[used++]; renderer.gameObject.SetActive(true); renderer.startColor = renderer.endColor = color;
            renderer.positionCount = points.Length; renderer.SetPositions(points);
        }
        protected override void OnCmpDisable() { foreach (var line in lines) if (line != null) line.gameObject.SetActive(false); base.OnCmpDisable(); }
        protected override void OnCleanUp()
        {
            foreach (var line in lines) if (line != null) Destroy(line.gameObject);
            lines.Clear(); if (material != null) Destroy(material); base.OnCleanUp();
        }
    }
    [HarmonyPatch(typeof(BuildToolHoverTextCard), nameof(BuildToolHoverTextCard.UpdateHoverElements))]
    internal static class MembraneConstructionHint
    {
        static void Postfix(BuildToolHoverTextCard __instance)
        {
            if (!MembraneFlowPreview.IsMembrane(__instance.currentDef) || BuildTool.Instance == null || PlayerController.Instance?.ActiveTool != BuildTool.Instance || BuildTool.Instance.visualizer == null) return;
            int origin = Grid.PosToCell(BuildTool.Instance.visualizer.transform.position);
            if (!Grid.IsValidCell(origin) || ClusterManager.Instance == null || Grid.WorldIdx[origin] != ClusterManager.Instance.activeWorldId) return;
            int world = Grid.WorldIdx[origin]; var orientation = BuildTool.Instance.GetBuildingOrientation;
            bool vertical = orientation != Orientation.Neutral;
            var reasons = new List<string>();
            foreach (int cell in MembraneFlowPreview.Placement(__instance.currentDef, origin, orientation))
            {
                var ends = FlowGuidance.Endpoints(cell, vertical, Grid.WidthInCells, Grid.CellCount);
                string text = FlowGuidance.Obstruction(vertical, MembraneFlowPreview.Blocked(ends.Item1, world, BuildTool.Instance.visualizer), MembraneFlowPreview.Blocked(ends.Item2, world, BuildTool.Instance.visualizer));
                if (!reasons.Contains(text)) reasons.Add(text);
            }
            // Append without resetting the native widget pools and instruction card.
            var drawer = HoverTextScreen.Instance?.drawer;
            if (drawer == null) return;
            drawer.BeginShadowBar();
            drawer.DrawText("分子筛输运轴：" + FlowGuidance.Axis(vertical), __instance.Styles_BodyText.Standard);
            drawer.NewLine(); drawer.DrawText(string.Join("；", reasons), __instance.Styles_BodyText.Standard);
            drawer.NewLine(); drawer.DrawText("按 " + GameUtil.GetActionString(global::Action.RotateBuilding) + " 旋转；建成后选介质并启用。", __instance.Styles_BodyText.Standard);
            drawer.EndShadowBar(); drawer.EndDrawing();
        }
    }
}
