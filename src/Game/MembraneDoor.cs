using System.Collections.Generic;
using System.Linq;
using KSerialization;
using HarmonyLib;
using UnityEngine;

namespace Baiye.SelectiveMembrane
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class MembraneDoor : KMonoBehaviour, INavDoor
    {
        public static readonly HashedString LockPort = new HashedString("BaiyeMembranePassage");
        [Serialize] public bool ManualLock;
        readonly HashSet<Navigator> travelers = new HashSet<Navigator>();
        readonly HashSet<Navigator> transitions = new HashSet<Navigator>();
        bool spawned, lastAllowed, lastPassable;
        int[] cells, floors;
        public new bool isSpawned => spawned;
        public int Travelers { get { travelers.RemoveWhere(n => n == null || !n.gameObject.activeInHierarchy || n.GetComponent<KPrefabID>()?.HasTag(GameTags.Dead) == true || n.GetComponent<KPrefabID>()?.HasTag(GameTags.Corpse) == true || (!transitions.Contains(n) && !Occupies(n))); return travelers.Count; } }
        public bool Wired => GetComponent<LogicPorts>()?.IsPortConnected(LockPort) == true;
        public bool AutomationAllows => !Wired || (GetComponent<LogicPorts>().GetInputValue(LockPort) & 1) != 0;
        public bool Allows => !ManualLock && AutomationAllows && GetComponent<MembraneBuilding>()?.State.Demolishing != true;
        bool Occupies(Navigator navigator) { int cell = Grid.PosToCell(navigator); return cells.Contains(cell) || cells.Contains(Grid.CellAbove(cell)); }
        internal bool Admitted(Navigator navigator) => travelers.Contains(navigator);
        internal void RecoverOccupant(Navigator navigator)
        {
            if (spawned && navigator != null && Occupies(navigator) && navigator.GetComponent<KPrefabID>()?.HasTag(GameTags.Corpse) != true) travelers.Add(navigator);
        }
        protected override void OnSpawn()
        {
            base.OnSpawn(); spawned = true;
            cells = GetComponent<Building>().PlacementCells.ToArray();
            floors = cells.Where(c => !cells.Contains(Grid.CellAbove(c))).ToArray();
            foreach (int cell in floors) Grid.FakeFloor.Add(cell);
            foreach (int cell in cells) { Grid.HasDoor[cell] = true; Grid.CritterImpassable[cell] = true; }
            RefreshNavigation(true);
            Subscribe(-801688580, _ => RefreshNavigation());
        }
        public void ToggleLock() { ManualLock = !ManualLock; RefreshNavigation(); }
        internal void RefreshNavigation(bool force = false)
        {
            if (!spawned) return;
            // Exit eligibility is rebuilt from real occupants, rather than
            // serializing transient Navigator references across a load.
            foreach (var identity in Components.LiveMinionIdentities.Items)
                if (identity != null) RecoverOccupant(identity.GetComponent<Navigator>());
            bool passable = Allows || Travelers != 0;
            if (!spawned || (!force && Allows == lastAllowed && passable == lastPassable)) return;
            lastAllowed = Allows;
            lastPassable = passable;
            foreach (int cell in cells)
            {
                // Only game-side navigation masks. SimCellOccupier remains the
                // sole owner of the solid element and impermeable properties.
                Game.Instance.SetDupePassableSolid(cell, passable, true);
                Grid.DupeImpassable[cell] = !passable;
                Pathfinding.Instance.AddDirtyNavGridCell(cell);
            }
            this.BoxingTrigger((int)GameHashes.DoorControlStateChanged, Allows ? Door.ControlState.Auto : Door.ControlState.Locked);
            if (!Allows && Travelers == 0) GetComponent<MembraneVisual>()?.SetPassage(false);
        }
        public bool IsOpen() => spawned && (Allows || Travelers != 0);
        public void Open() { if (spawned) GetComponent<MembraneVisual>()?.SetPassage(true); }
        public void Close() { if (spawned && Travelers == 0) GetComponent<MembraneVisual>()?.SetPassage(false); }
        internal void Enter(Navigator navigator) { travelers.Add(navigator); transitions.Add(navigator); }
        internal void Leave(Navigator navigator) { transitions.Remove(navigator); if (!Occupies(navigator)) travelers.Remove(navigator); RefreshNavigation(); Close(); }
        protected override void OnCleanUp()
        {
            spawned = false;
            foreach (int cell in floors ?? new int[0]) Grid.FakeFloor.Remove(cell);
            foreach (int cell in cells ?? new int[0])
            {
                Grid.HasDoor[cell] = false; Grid.CritterImpassable[cell] = false; Grid.DupeImpassable[cell] = false;
                Game.Instance.SetDupePassableSolid(cell, false, Grid.Solid[cell]); Pathfinding.Instance.AddDirtyNavGridCell(cell);
            }
            travelers.Clear(); transitions.Clear(); base.OnCleanUp();
        }
    }
    [HarmonyPatch(typeof(DoorTransitionLayer), nameof(DoorTransitionLayer.BeginTransition))]
    internal static class MembraneDoorEnter
    {
        static void Postfix(Navigator navigator, List<INavDoor> ___doors)
        { foreach (var door in ___doors.OfType<MembraneDoor>()) door.Enter(navigator); }
    }
    [HarmonyPatch(typeof(DoorTransitionLayer), nameof(DoorTransitionLayer.EndTransition))]
    internal static class MembraneDoorLeave
    {
        static void Prefix(Navigator navigator, List<INavDoor> ___doors)
        { foreach (var door in ___doors.OfType<MembraneDoor>()) door.Leave(navigator); }
    }
    [HarmonyPatch(typeof(Navigator), nameof(Navigator.BeginTransition))]
    internal static class MembraneNewPassageGuard
    {
        // During draining/locking, existing travelers keep game-side masks for
        // safe exit. Reject all other starts even if an old path is cached.
        static bool Prefix(Navigator __instance, NavGrid.Transition transition)
        {
            int current = Grid.PosToCell(__instance), dest = Grid.OffsetCell(current, transition.x, transition.y);
            var cells = transition.voidOffsets.Select(offset => Grid.OffsetCell(current, offset)).Concat(new[] { dest, Grid.CellAbove(dest) });
            foreach (int cell in cells.Where(Grid.IsValidCell))
            {
                var door = Grid.Objects[cell, (int)ObjectLayer.Building]?.GetComponent<MembraneDoor>();
                if (door == null || door.Admitted(__instance)) continue;
                if (!door.Allows || __instance.GetComponent<MinionIdentity>() == null) { __instance.Stop(); return false; }
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(MinionIdentity), "OnSpawn")]
    internal static class MembraneLoadedOccupant
    {
        static void Postfix(MinionIdentity __instance)
        {
            var navigator = __instance.GetComponent<Navigator>();
            if (navigator == null) return;
            int cell = Grid.PosToCell(navigator);
            foreach (int occupied in new[] { cell, Grid.CellAbove(cell) }.Where(Grid.IsValidCell))
            {
                var door = Grid.Objects[occupied, (int)ObjectLayer.Building]?.GetComponent<MembraneDoor>();
                if (door == null) continue;
                door.RecoverOccupant(navigator); door.RefreshNavigation(true);
            }
        }
    }
}
