using System.Collections.Generic;
using Baiye.SelectiveMembrane.Core;
using UnityEngine;

namespace Baiye.SelectiveMembrane
{
    // Completed-building art only. Never owns Sim cells, permissions or power.
    public sealed class MembraneVisual : KMonoBehaviour, ISim200ms
    {
        public const string TileAnim = "baiye_membrane_tile_kanim";
        public const string DoorAnim = "baiye_membrane_door_kanim";
        static readonly Dictionary<int, MembraneVisual> Nodes = new Dictionary<int, MembraneVisual>();
        MembraneBuilding membrane;
        MembraneDoor door;
        KBatchedAnimController controller;
        int[] cells;
        int mask = -1, upperMask = -1;
        bool spawned, passage;
        float pulse;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            membrane = GetComponent<MembraneBuilding>();
            door = GetComponent<MembraneDoor>();
            controller = GetComponent<KBatchedAnimController>();
            cells = GetComponent<Building>().PlacementCells;
            spawned = true;
            foreach (int cell in cells) Nodes[cell] = this;
            if (door != null)
            {
                passage = door.Travelers != 0;
                controller.Play(VisualPolicy.DoorClip(passage, false));
            }
            RefreshLocal();
            NotifyNeighbors();
            RefreshIndicators();
        }

        internal void SetPassage(bool value)
        {
            if (!spawned || door == null || passage == value) return;
            passage = value;
            controller.Play(VisualPolicy.DoorClip(value, true), KAnim.PlayMode.Once);
            controller.Queue(VisualPolicy.DoorClip(value, false), KAnim.PlayMode.Once);
        }

        void RefreshLocal()
        {
            if (!spawned) return;
            int world = Grid.WorldIdx[cells[0]];
            int turns = (int)(GetComponent<Rotatable>()?.GetOrientation() ?? Orientation.Neutral);
            int local(int origin) => VisualPolicy.LocalMask(VisualPolicy.Mask(origin, Grid.WidthInCells, Grid.CellCount,
                cell => Nodes.TryGetValue(cell, out var other) && other != this && other.spawned && Grid.WorldIdx[cell] == world), turns);
            // R90 maps local up to world right. In the two allowed orientations
            // the lower art unit is the smaller grid index, even if native
            // PlacementCells enumeration order changes.
            int lower = door == null ? cells[0] : System.Math.Min(cells[0], cells[1]);
            int next = local(lower);
            if (door != null)
            {
                int top = local(System.Math.Max(cells[0], cells[1]));
                if (next == mask && top == upperMask) return;
                mask = next; upperMask = top;
                Join("join_west_lower", (next & 1) != 0); Join("join_east_lower", (next & 2) != 0);
                Join("join_west_upper", (top & 1) != 0); Join("join_east_upper", (top & 2) != 0);
                Join("join_south", (next & 4) != 0); Join("join_north", (top & 8) != 0);
                return;
            }
            if (next == mask) return;
            mask = next;
            controller.Play("tile_" + mask);
            RefreshIndicators();
        }

        void Join(string symbol, bool enabled) => controller.SetSymbolTint(symbol, new Color(1, 1, 1, enabled ? 1 : 0));

        void NotifyNeighbors()
        {
            var touched = new HashSet<MembraneVisual>();
            foreach (int cell in cells)
                for (int bit = 1; bit <= 8; bit <<= 1)
                {
                    int other = VisualPolicy.Neighbor(cell, bit, Grid.WidthInCells, Grid.CellCount);
                    if (other >= 0 && Nodes.TryGetValue(other, out var visual) && visual != this) touched.Add(visual);
                }
            foreach (var visual in touched) visual.RefreshLocal();
        }

        void RefreshIndicators()
        {
            if (!spawned || !membrane.Ready) return;
            var signal = VisualPolicy.Signal(membrane.State, membrane.Network?.Demand > 0,
                membrane.Network?.Fraction > 0, membrane.RecentRate);
            Color tint;
            switch (signal)
            {
                case VisualSignal.Off: tint = new Color(.28f, .32f, .35f, 1); break;
                case VisualSignal.Paused:
                case VisualSignal.Draining: tint = new Color(1, .65f, .3f, 1); break;
                case VisualSignal.Waiting: tint = new Color(.7f, .5f, .34f, 1); break;
                case VisualSignal.Working: tint = new Color(.85f + .1f * Mathf.Sin(pulse), 1, 1, 1); break;
                default: tint = new Color(.65f, .8f, .85f, 1); break;
            }
            controller.SetSymbolTint("status", tint);
            bool active = signal == VisualSignal.Working && membrane.State.Applied.Active;
            int worldDirection = membrane.Vertical ? 8 : 2;
            if (membrane.State.Applied.Direction < 0) worldDirection = membrane.Vertical ? 4 : 1;
            int turns = (int)(GetComponent<Rotatable>()?.GetOrientation() ?? Orientation.Neutral);
            bool forward = (VisualPolicy.LocalMask(worldDirection, turns) & (2 | 8)) != 0;
            controller.SetSymbolTint("flow_forward", new Color(1, 1, 1, active && forward ? .7f : 0));
            controller.SetSymbolTint("flow_reverse", new Color(1, 1, 1, active && !forward ? .7f : 0));
        }

        public void Sim200ms(float dt)
        {
            pulse += dt * 3;
            // Reconcile restored occupants and travelers pruned after death/despawn.
            if (door != null) SetPassage(door.Travelers != 0);
            RefreshIndicators();
        }

        protected override void OnCleanUp()
        {
            spawned = false;
            if (cells != null)
            {
                foreach (int cell in cells)
                    if (Nodes.TryGetValue(cell, out var visual) && visual == this) Nodes.Remove(cell);
                NotifyNeighbors();
            }
            base.OnCleanUp();
        }
        internal static void Reset() => Nodes.Clear();
    }
}
