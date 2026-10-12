using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Baiye.SelectiveMembrane.Core;

namespace Baiye.SelectiveMembrane
{
    // CircuitManager owns payment, overload and reports. Standby consumers have
    // zero demand; membranes never connect separate electrical networks.
    internal sealed class MembranePowerInput : IEnergyConsumer
    {
        internal readonly MembraneBuilding Building;
        public int PowerCell { get; }
        public bool IsVirtual => false;
        public object VirtualCircuitKey => null;
        internal float Demand;
        internal double Paid;
        public float WattsUsed => Demand;
        public float WattsNeededWhenActive => Demand;
        public int PowerSortOrder => 0;
        public string Name => MembraneStrings.Text("选择性分子筛网络", "Selective membrane network");
        public bool IsConnected { get; private set; }
        public bool IsPowered { get; private set; }
        public void SetConnectionStatus(CircuitManager.ConnectionStatus status)
        {
            IsConnected = status != CircuitManager.ConnectionStatus.NotConnected;
            IsPowered = status == CircuitManager.ConnectionStatus.Powered;
        }
        internal MembranePowerInput(MembraneBuilding owner, int cell) { Building = owner; PowerCell = cell; }
    }
    internal sealed class MembraneNetwork
    {
        internal MembraneBuilding[] Buildings;
        internal SourceElection Election = new SourceElection();
        internal MembranePowerInput Main;
        internal double Demand, Paid, Fraction;
        internal InputCandidate[] Candidates = Array.Empty<InputCandidate>();
        readonly List<System.Tuple<MembraneBuilding, int, WorkPlan>> work = new List<System.Tuple<MembraneBuilding, int, WorkPlan>>();
        internal int UnitCount => Buildings.Sum(b => b.Units.Length);
        internal void Prepare(float dt, CircuitManager manager)
        {
            work.Clear(); Demand = 0;
            foreach (var b in Buildings.Where(b => b.Ready))
            {
                b.Prepare();
                for (int i = 0; i < b.Units.Length; i++)
                {
                    var plan = b.Plan(i, dt); Demand += plan.Watts;
                    work.Add(Tuple.Create(b, i, plan));
                }
            }
            var inputs = Buildings.SelectMany(b => b.Inputs).ToArray();
            Candidates = NetworkPolicy.Deduplicate(inputs.Select(input => new InputCandidate { Node = input.Building.GetInstanceID(),
                World = input.Building.World, Cell = input.PowerCell, Priority = input.Building.State.InputPriority, Circuit = manager.GetCircuitID(input.PowerCell) }));
            int cell = Election.Choose(Candidates, Fraction, dt, Demand > 0);
            Main = inputs.FirstOrDefault(input => input.PowerCell == cell);
            foreach (var input in inputs) { input.Paid = 0; input.Demand = input == Main ? (float)Demand : 0; }
        }
        internal void Settle(float dt)
        {
            Paid = Main?.Paid ?? 0;
            Fraction = NetworkPolicy.Fraction(Paid, Demand, dt);
            foreach (var item in work)
                item.Item1.Execute(item.Item2, item.Item3, item.Item3.Watts == 0 ? 1 : Fraction);
        }
    }
    internal static class MembraneNetworks
    {
        internal static readonly Dictionary<int, MembraneBuilding> Buildings = new Dictionary<int, MembraneBuilding>();
        internal static List<MembraneNetwork> Networks = new List<MembraneNetwork>();
        static bool dirty;
        internal static void Add(MembraneBuilding building)
        {
            Buildings[building.GetInstanceID()] = building; dirty = true;
            foreach (var input in building.Inputs) Game.Instance.circuitManager.Connect(input);
        }
        internal static void Remove(MembraneBuilding building)
        {
            Buildings.Remove(building.GetInstanceID()); dirty = true;
            foreach (var input in building.Inputs) Game.Instance.circuitManager.Disconnect(input, true);
        }
        internal static void Prepare(float dt, CircuitManager manager)
        {
            if (dirty)
            {
                var old = Networks; var used = new HashSet<MembraneNetwork>();
                Networks = NetworkPolicy.Components(Buildings.Values.Select(b => new NetworkNode { Id = b.GetInstanceID(), World = b.World, Cells = b.Cells }), Grid.WidthInCells)
                    .Select(ids => {
                        var members = ids.Select(id => Buildings[id]).ToArray();
                        // On split, only the component containing the elected
                        // input inherits its election. Never copy a paid budget.
                        var previous = old.FirstOrDefault(n => !used.Contains(n) && members.Any(b => b.Cells.Contains(n.Election.CurrentCell)));
                        var network = new MembraneNetwork { Buildings = members };
                        if (previous != null) { used.Add(previous); network.Election = previous.Election; network.Fraction = previous.Fraction; }
                        foreach (var b in members) b.Network = network;
                        return network;
                    }).ToList();
                dirty = false;
            }
            foreach (var network in Networks) network.Prepare(dt, manager);
        }
        internal static void Reset() { Buildings.Clear(); Networks.Clear(); dirty = false; NativeTransfer.Reset(); MembraneVisual.Reset(); }
    }
    [HarmonyPatch(typeof(CircuitManager), nameof(CircuitManager.Sim200msLast))]
    internal static class MembranePowerTick
    {
        static void Prefix(CircuitManager __instance, float dt) { if (dt > 0 && !NativeTransfer.Saving) MembraneNetworks.Prepare(dt, __instance); }
        static void Postfix(float dt) { if (dt > 0 && !NativeTransfer.Saving) foreach (var network in MembraneNetworks.Networks) network.Settle(dt); }
    }
    [HarmonyPatch]
    internal static class MembraneActualPayment
    {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(CircuitManager), "PowerFromGenerator");
            yield return AccessTools.Method(typeof(CircuitManager), "PowerFromBatteries");
        }
        static void Prefix(float joules_needed, out float __state) { __state = joules_needed; }
        static void Postfix(IEnergyConsumer c, float __result, float __state)
        {
            if (c is MembranePowerInput input) input.Paid += Math.Max(0, __state - __result);
        }
    }
}
