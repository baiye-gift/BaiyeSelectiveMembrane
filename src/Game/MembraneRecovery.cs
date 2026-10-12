using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using KSerialization;
using Newtonsoft.Json;
using HarmonyLib;
using Baiye.SelectiveMembrane.Core;

namespace Baiye.SelectiveMembrane
{
    public sealed class RecoveryStock { public int World; public TransferLedger Ledger; }
    [SerializationConfig(KSerialization.MemberSerialization.OptIn)]
    public sealed class MembraneRecovery : KMonoBehaviour, ISim200ms
    {
        internal static MembraneRecovery Instance;
        [Serialize] string snapshot;
        List<RecoveryStock> stock = new List<RecoveryStock>();
        protected override void OnSpawn()
        {
            base.OnSpawn(); Instance = this;
            if (!string.IsNullOrEmpty(snapshot)) stock = JsonConvert.DeserializeObject<List<RecoveryStock>>(snapshot);
            foreach (var item in stock) item.Ledger.ValidateForSave();
        }
        [OnSerializing] void Serialize()
        {
            foreach (var item in stock) item.Ledger.ValidateForSave();
            snapshot = JsonConvert.SerializeObject(stock);
        }
        internal void Adopt(TransferLedger ledger, int world) { stock.Add(new RecoveryStock { Ledger = ledger, World = world }); }
        public void Sim200ms(float dt)
        {
            if (NativeTransfer.Saving) return;
            foreach (var item in stock)
            {
                var ledger = item.Ledger;
                foreach (int target in new[] { ledger.Source, ledger.Target })
                {
                    var cell = NativeTransfer.Read(target);
                    if (cell.World == item.World && cell.Accepts(ledger.Element))
                    { NativeTransfer.Emit(ledger, target, ledger.Mass, double.PositiveInfinity); break; }
                }
            }
            stock.RemoveAll(item => item.Ledger.Empty);
        }
        protected override void OnCleanUp() { Instance = null; MembraneNetworks.Reset(); base.OnCleanUp(); }
    }
    [HarmonyPatch(typeof(SaveGame), "OnPrefabInit")]
    internal static class MembraneRecoveryRegistration { static void Postfix(SaveGame __instance) => __instance.gameObject.AddOrGet<MembraneRecovery>(); }
}
