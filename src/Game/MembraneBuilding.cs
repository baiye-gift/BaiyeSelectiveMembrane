using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using KSerialization;
using Newtonsoft.Json;
using HarmonyLib;
using UnityEngine;
using Baiye.SelectiveMembrane.Core;

namespace Baiye.SelectiveMembrane
{
    public sealed class SavedBuilding
    {
        public int Schema = 1;
        public BuildingState State = new BuildingState();
        public TransferLedger[] Units;
    }
    [SerializationConfig(KSerialization.MemberSerialization.OptIn)]
    public sealed class MembraneBuilding : KMonoBehaviour
    {
        [Serialize] string snapshot;
        public BuildingState State { get; private set; } = new BuildingState();
        public TransferLedger[] Units { get; private set; }
        public int[] Cells { get; private set; }
        public bool Vertical { get; private set; }
        public int World { get; private set; }
        public bool Ready { get; private set; }
        internal bool EarlyDemolition;
        internal MembraneNetwork Network;
        internal readonly List<MembranePowerInput> Inputs = new List<MembranePowerInput>();
        public string LastCopyResult = "";
        readonly Queue<System.Tuple<float, double>> deliveries = new Queue<System.Tuple<float, double>>();
        public double RecentRate { get { TrimDeliveries(); return deliveries.Sum(d => d.Item2) / 5; } }
        protected override void OnSpawn()
        {
            base.OnSpawn();
            Cells = GetComponent<Building>().PlacementCells.ToArray();
            World = Grid.WorldIdx[Cells[0]];
            bool rotated = GetComponent<Rotatable>()?.GetOrientation() != Orientation.Neutral;
            Vertical = rotated;
            Units = Cells.Select(_ => new TransferLedger()).ToArray();
            if (!string.IsNullOrEmpty(snapshot))
            {
                var saved = JsonConvert.DeserializeObject<SavedBuilding>(snapshot);
                if (saved.Schema != 1 || saved.Units.Length != Cells.Length || !saved.State.Applied.Valid) throw new InvalidOperationException("Unsupported membrane save state");
                State = saved.State; Units = saved.Units;
                foreach (var unit in Units) unit.ValidateForSave();
            }
            if (!State.Applied.Active) foreach (var unit in Units) if (!unit.Empty) NativeTransfer.Passive.Claim(unit, unit.Source, unit.Target);
            if (EarlyDemolition) State.Demolishing = true;
            Subscribe((int)GameHashes.CopySettings, CopySettings);
            foreach (int cell in Cells) Inputs.Add(new MembranePowerInput(this, cell));
            MembraneNetworks.Add(this); Ready = true;
        }
        [OnSerializing] void Serialize()
        {
            foreach (var unit in Units) unit.ValidateForSave();
            snapshot = JsonConvert.SerializeObject(new SavedBuilding { State = State, Units = Units });
        }
        protected override void OnCleanUp()
        {
            Ready = false; MembraneNetworks.Remove(this);
            if (MembraneRecovery.Instance != null)
                foreach (var unit in Units ?? Array.Empty<TransferLedger>()) if (!unit.Empty) MembraneRecovery.Instance.Adopt(unit, World);
            base.OnCleanUp();
        }
        public System.Tuple<int, int> Endpoints(int index)
        {
            return FlowGuidance.Endpoints(Cells[index], Vertical, Grid.WidthInCells, Grid.CellCount);
        }
        internal WorkPlan Plan(int index, double dt)
        {
            var endpoints = Endpoints(index);
            var a = NativeTransfer.Read(endpoints.Item1); var b = NativeTransfer.Read(endpoints.Item2);
            if (!Ready || a.World != World || b.World != World || Cells.Any(c => !Grid.Solid[c])) return new WorkPlan();
            if (!Units[index].Empty)
            {
                var loaded = NativeTransfer.Resolve(Units[index].Element);
                if (loaded == null || loaded.IsLiquid != (Units[index].Phase == MediumPhase.Liquid)) return new WorkPlan();
            }
            return TransferPolicy.Plan(Units[index], State, a, b, dt, NativeTransfer.Read);
        }
        internal void Execute(int index, WorkPlan paid, double fraction)
        {
            if (!Ready || NativeTransfer.Saving || fraction <= 0) return;
            var fresh = Plan(index, .2);
            if (paid.Element != fresh.Element || paid.Source != fresh.Source || paid.Target != fresh.Target) return;
            var unit = Units[index];
            if (paid.Output > 0 && fresh.Output > 0)
                NativeTransfer.Emit(unit, fresh.Target, Math.Min(paid.Output * fraction, fresh.Output), State.Demolishing ? double.PositiveInfinity : TransferPolicy.Headroom(State.Applied, NativeTransfer.Read(fresh.Target), unit.Phase), Delivered);
            if (paid.Intake > 0 && fresh.Intake > 0)
            {
                fresh.Intake = Math.Min(paid.Intake * fraction, fresh.Intake);
                NativeTransfer.Intake(unit, fresh);
            }
        }
        void Delivered(double amount) { deliveries.Enqueue(Tuple.Create(Time.unscaledTime, amount)); TrimDeliveries(); }
        void TrimDeliveries() { while (deliveries.Count > 0 && Time.unscaledTime - deliveries.Peek().Item1 > 5) deliveries.Dequeue(); }
        public bool Apply(Core.MembraneSettings cfg) => State.Request(cfg, Units);
        public void Pause() { State.Paused = !State.Paused; }
        public void CancelDemolition() { State.Demolishing = false; GetComponent<Deconstructable>().CancelDeconstruction(); GetComponent<MembraneDoor>()?.RefreshNavigation(true); }
        void CopySettings(object data)
        {
            var source = (data as GameObject)?.GetComponent<MembraneBuilding>();
            if (source == null || source == this) return;
            var copied = source.State.Applied.Clone();
            if (source.Vertical != Vertical) { copied.Direction = State.Applied.Direction; LastCopyResult = "已复制；输运轴不同，保留原方向"; }
            else LastCopyResult = "已复制当前应用配置";
            if (!Apply(copied)) LastCopyResult = "已有待应用配置，已跳过";
        }
        internal void Prepare()
        {
            State.Settle(Units);
            GetComponent<MembraneDoor>()?.RefreshNavigation();
            if (State.Demolishing && Units.All(u => u.Empty) && (GetComponent<MembraneDoor>()?.Travelers ?? 0) == 0)
            {
                var demolition = GetComponent<Deconstructable>();
                MembraneDeconstruction.Releasing = true;
                try
                {
                    GetComponent<Prioritizable>()?.SetMasterPriority(new PrioritySetting((PriorityScreen.PriorityClass)State.DemolitionPriorityClass, State.DemolitionPriority));
                    demolition.QueueDeconstruction(false);
                }
                finally { MembraneDeconstruction.Releasing = false; }
            }
        }
        public string Status()
        {
            if (State.Demolishing) return "等待安全退料后拆除";
            if (State.Paused) return "筛分已暂停";
            if (State.Pending != null) return "排空旧库存后应用配置";
            if (State.Applied.Allowed.Count == 0) return "请先选择允许介质";
            if (Units.Any(u => u.InFlight)) return "等待原生转移回调";
            if (Units.Any(u => !u.Empty && NativeTransfer.Resolve(u.Element) == null)) return "缓存介质对应的模组未加载；库存已保留";
            if (State.Applied.Active && Network?.Demand > 0 && Network.Fraction <= 0) return "等待供电";
            for (int i = 0; i < Units.Length; i++)
                if (Plan(i, .2).HasWork) return RecentRate > 0 ? $"正在筛分 · {RecentRate:F3} kg/s" : "已就绪；等待原生输运";
            var reasons = new List<string>();
            for (int i = 0; i < Units.Length; i++)
            {
                var ends = Endpoints(i); var a = NativeTransfer.Read(ends.Item1); var b = NativeTransfer.Read(ends.Item2);
                string reason = Units[i].Empty && (a.Solid || b.Solid) ? FlowGuidance.Obstruction(Vertical, a.Solid, b.Solid) : FlowGuidance.Waiting(Units[i], State, a, b, World, NativeTransfer.Read);
                if (!reasons.Contains(reason)) reasons.Add(reason);
            }
            return string.Join("；", reasons);
        }
    }
    [HarmonyPatch(typeof(AccessControl), "OnCopySettings")]
    internal static class MembranePermissionCopyGuard
    {
        static bool Prefix(AccessControl __instance, object data) => __instance.GetComponent<MembraneBuilding>() == null;
    }
    [HarmonyPatch(typeof(Deconstructable), nameof(Deconstructable.QueueDeconstruction), new[] { typeof(bool) })]
    internal static class MembraneDeconstruction
    {
        internal static bool Releasing;
        static bool Prefix(Deconstructable __instance)
        {
            var membrane = __instance.GetComponent<MembraneBuilding>();
            if (membrane == null || Releasing) return true;
            // Native Deconstructable may restore its marked order before our
            // component's OnSpawn has reconstructed the saved unit ledgers.
            if (!membrane.Ready) { membrane.EarlyDemolition = true; return false; }
            if (!membrane.State.Demolishing)
            {
                var priority = membrane.GetComponent<Prioritizable>()?.GetMasterPriority() ?? new PrioritySetting(PriorityScreen.PriorityClass.basic, 5);
                membrane.State.DemolitionPriority = priority.priority_value;
                membrane.State.DemolitionPriorityClass = (int)priority.priority_class;
            }
            membrane.State.Demolishing = true;
            membrane.GetComponent<MembraneDoor>()?.RefreshNavigation(true);
            return membrane.Units.All(u => u.Empty) && (membrane.GetComponent<MembraneDoor>()?.Travelers ?? 0) == 0;
        }
    }
    [HarmonyPatch(typeof(Deconstructable), "OnCompleteWork")]
    internal static class MembraneDemolitionCompleteGuard
    {
        static bool Prefix(Deconstructable __instance)
        {
            var membrane = __instance.GetComponent<MembraneBuilding>();
            return membrane == null || (membrane.Units.All(u => u.Empty) && (membrane.GetComponent<MembraneDoor>()?.Travelers ?? 0) == 0);
        }
    }
    [HarmonyPatch(typeof(Deconstructable), "CancelDeconstruction")]
    internal static class MembraneCancelDeconstruction
    {
        static void Postfix(Deconstructable __instance) { var membrane = __instance.GetComponent<MembraneBuilding>(); if (membrane != null) membrane.State.Demolishing = false; }
    }
    [HarmonyPatch(typeof(WorldDamage), nameof(WorldDamage.ApplyDamage), new[] { typeof(Sim.WorldDamageInfo) })]
    internal static class MembranePressureImmunity
    {
        static bool Prefix(Sim.WorldDamageInfo damage_info, ref float __result)
        {
            if (!Grid.IsValidCell(damage_info.gameCell) || Grid.Objects[damage_info.gameCell, (int)ObjectLayer.Building]?.GetComponent<MembraneBuilding>() == null) return true;
            __result = 0; return false;
        }
    }
}
