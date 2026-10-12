using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Baiye.SelectiveMembrane.Core;

namespace Baiye.SelectiveMembrane
{
    internal static class NativeTransfer
    {
        internal static bool Saving;
        internal static int Pending;
        // Coordinate competing membrane emissions; native EmitMass also returns
        // a failure callback, as used by the shipped Turbine implementation.
        static readonly Dictionary<int, Reservation> targets = new Dictionary<int, Reservation>();
        internal static readonly PassiveClaims Passive = new PassiveClaims();
        sealed class Reservation { public string Element; public int Count; public double Mass; }
        public static CellSample Read(int cell)
        {
            if (!Grid.IsValidCell(cell)) return new CellSample { Cell = cell, World = -1 };
            var element = Grid.Element[cell];
            return new CellSample { Cell = cell, World = Grid.WorldIdx[cell], Y = cell / Grid.WidthInCells,
                Valid = Grid.WorldIdx[cell] != 255 && element != null, Solid = Grid.Solid[cell] || element?.IsSolid == true,
                Element = element?.id.ToString(), Phase = element?.IsLiquid == true ? MediumPhase.Liquid : MediumPhase.Gas, Mass = Grid.Mass[cell] };
        }
        public static float NativeAmount(double amount)
        {
            float value = (float)amount;
            if (value > amount && value > 0) value = BitConverter.ToSingle(BitConverter.GetBytes(BitConverter.ToInt32(BitConverter.GetBytes(value), 0) - 1), 0);
            return value;
        }
        public static void Intake(TransferLedger ledger, WorkPlan plan)
        {
            if (Saving || MembraneRecovery.Instance == null) return;
            var source = Read(plan.Source);
            if (!source.Valid || source.Solid || source.Element != plan.Element || source.World != Read(plan.Target).World) return;
            var element = Resolve(plan.Element);
            if (element == null) return;
            if (plan.Passive && !Passive.Claim(ledger, plan.Source, plan.Target)) return;
            float amount = NativeAmount(Math.Min(plan.Intake, source.Mass));
            long id = ledger.BeginIntake(plan.Element, plan.Phase, amount, plan.Source, plan.Target, Grid.DiseaseIdx[plan.Source]);
            if (id == 0) { Passive.Settle(ledger); return; }
            Pending++;
            var handle = Game.Instance.massConsumedCallbackManager.Add((info, data) => {
                // The closure owns this ledger even if its building has melted.
                // A replacement building never receives the old callback.
                if (info.mass > 0 && ElementLoader.elements[info.elemIdx].id.ToString() != ledger.Element)
                {
                    var actual = ElementLoader.elements[info.elemIdx];
                    var recovery = new TransferLedger { Element = actual.id.ToString(), Phase = actual.IsLiquid ? MediumPhase.Liquid : MediumPhase.Gas, Source = ledger.Source, Target = ledger.Target };
                    recovery.Parcels.Add(new Parcel { Mass = info.mass, Temperature = info.temperature, Disease = info.diseaseIdx, Germs = info.diseaseCount });
                    MembraneRecovery.Instance.Adopt(recovery, source.World);
                    ledger.AcceptIntake(id, 0, 0, 255, 0); Pending--; Passive.Settle(ledger);
                    UnityEngine.Debug.LogWarning("[BaiyeSelectiveMembrane] Unexpected consumed element retained by world recovery."); return;
                }
                if (ledger.AcceptIntake(id, info.mass, info.temperature, info.diseaseIdx, info.diseaseCount)) { Pending--; Passive.Settle(ledger); }
            }, null, "BaiyeMembraneIntake");
            try { SimMessages.ConsumeMass(plan.Source, element.id, amount, (byte)0, handle.index); }
            catch (Exception error) { Game.Instance.massConsumedCallbackManager.Release(handle, "BaiyeMembraneIntakeNotSubmitted"); ledger.AcceptIntake(id, 0, 0, 255, 0); Pending--; Passive.Settle(ledger); UnityEngine.Debug.LogWarning("[BaiyeSelectiveMembrane] Intake was not submitted: " + error.Message); }
        }
        public static void Emit(TransferLedger ledger, int target, double maximum, double headroom, System.Action<double> delivered = null)
        {
            if (Saving || ledger.Empty || ledger.Emission != null) return;
            var element = Resolve(ledger.Element);
            if (element == null) return; // Missing mod element: retain stock without reserving a callback.
            if (element.IsLiquid != (ledger.Phase == MediumPhase.Liquid)) return;
            var cell = Read(target);
            if (!cell.Accepts(ledger.Element) || cell.World != Read(ledger.Source).World) return;
            if (targets.TryGetValue(target, out var prior) && prior.Element != ledger.Element) return;
            double reserved = prior?.Mass ?? 0;
            // Clamp to the first physical parcel *before* converting to native
            // float. A subtraction tail may not itself be float-representable.
            float amount = NativeAmount(Math.Min(ledger.Parcels[0].Mass, Math.Min(maximum, Math.Max(0, headroom - reserved))));
            var ticket = ledger.BeginEmit(amount); if (ticket == null) return;
            if (prior == null) targets[target] = prior = new Reservation { Element = ledger.Element };
            prior.Count++; prior.Mass += ticket.Mass;
            Pending++;
            var game = Game.Instance;
            HandleVector<Game.ComplexCallbackInfo<Sim.MassEmittedCallback>>.Handle handle = default;
            handle = game.massEmitCallbackManager.Add((info, data) => {
                bool success = info.suceeded == 1;
                if (!ledger.FinishEmit(ticket.Id, success)) return;
                game.massEmitCallbackManager.Release(handle, "BaiyeMembraneEmitSettled");
                Pending--; prior.Mass -= ticket.Mass; prior.Count--;
                if (prior.Count == 0) targets.Remove(target);
                Passive.Settle(ledger);
                if (success) delivered?.Invoke(ticket.Mass);
            }, null, "BaiyeMembraneEmit");
            try { SimMessages.EmitMass(target, element.idx, (float)ticket.Mass, (float)ticket.Temperature, ticket.Disease, ticket.Germs, handle.index); }
            catch (Exception error)
            {
                game.massEmitCallbackManager.Release(handle, "BaiyeMembraneEmitNotSubmitted"); ledger.FinishEmit(ticket.Id, false); Pending--;
                prior.Mass -= ticket.Mass; if (--prior.Count == 0) targets.Remove(target);
                UnityEngine.Debug.LogWarning("[BaiyeSelectiveMembrane] Emission was not submitted: " + error.Message);
            }
        }
        internal static Element Resolve(string id) => ResolveLoaded(ElementLoader.elements, id);
        internal static Element ResolveLoaded(IEnumerable<Element> loaded, string id) => loaded.FirstOrDefault(e => e.id.ToString() == id && (e.IsGas || e.IsLiquid) && e.id != SimHashes.Vacuum);
        internal static void SaveBarrier()
        {
            Saving = true;
            try
            {
                var flush = AccessTools.Method(typeof(Game), "UnsafeSim200ms");
                for (int attempt = 0; Pending != 0 && attempt < 8; attempt++) flush.Invoke(Game.Instance, new object[] { 0f });
                if (Pending != 0) throw new InvalidOperationException("Membrane native callbacks did not settle; save was stopped before serializing inconsistent stock");
            }
            finally { Saving = false; }
        }
        internal static void Reset() { Pending = 0; Saving = false; targets.Clear(); Passive.Clear(); }
    }
    [HarmonyPatch(typeof(SaveLoader), "Save", new[] { typeof(System.IO.BinaryWriter) })]
    internal static class MembraneSaveBarrier { static void Prefix() => NativeTransfer.SaveBarrier(); }
}
