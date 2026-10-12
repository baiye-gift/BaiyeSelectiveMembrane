using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;

namespace Baiye.SelectiveMembrane.Core
{
    public sealed class MembraneSettings
    {
        public bool Active;
        public int Direction = 1;
        public HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal);
        public double? GasLimit, LiquidLimit;
        public MembraneSettings Clone() => new MembraneSettings { Active = Active, Direction = Direction, Allowed = new HashSet<string>(Allowed, StringComparer.Ordinal), GasLimit = GasLimit, LiquidLimit = LiquidLimit };
        public bool Valid => (Direction == 1 || Direction == -1) && LimitValid(GasLimit) && LimitValid(LiquidLimit) && Allowed != null;
        static bool LimitValid(double? limit) => !limit.HasValue || (TransferLedger.Finite(limit.Value) && limit.Value >= 0);
        public bool Equivalent(MembraneSettings other) => other != null && Active == other.Active && Direction == other.Direction && GasLimit == other.GasLimit && LiquidLimit == other.LiquidLimit && Allowed.SetEquals(other.Allowed);
        public bool NeedsDrain(MembraneSettings next) => Active != next.Active || Direction != next.Direction || !Allowed.IsSubsetOf(next.Allowed) || Lower(GasLimit, next.GasLimit) || Lower(LiquidLimit, next.LiquidLimit);
        static bool Lower(double? old, double? next) => next.HasValue && (!old.HasValue || next.Value < old.Value);
        public static bool TryLimit(string text, CultureInfo culture, out double value) => double.TryParse(text, NumberStyles.Float, culture, out value) && TransferLedger.Finite(value) && value >= 0;
    }
    public sealed class BuildingState
    {
        public MembraneSettings Applied = new MembraneSettings(), Pending;
        public bool Paused, Demolishing;
        public int Revision, InputPriority = 5;
        public int DemolitionPriority = 5, DemolitionPriorityClass;
        public bool Request(MembraneSettings next, IEnumerable<TransferLedger> units)
        {
            if (Pending != null || !next.Valid) return false;
            if (Applied.NeedsDrain(next) && units.Any(u => !u.Empty)) Pending = next.Clone();
            else Applied = next.Clone();
            Revision++;
            return true;
        }
        public bool Settle(IEnumerable<TransferLedger> units)
        {
            if (Pending == null || units.Any(u => !u.Empty)) return false;
            Applied = Pending; Pending = null; Revision++; return true;
        }
        public void CancelPending() { Pending = null; }
    }
    public sealed class EditSession
    {
        public MembraneSettings Draft;
        public int BaseRevision;
        public bool Stale;
        public string GasText, LiquidText, PriorityText;
        double? baseGas, baseLiquid;
        public EditSession(BuildingState state) { Reset(state); }
        public void Reset(BuildingState state) { Draft = (state.Pending ?? state.Applied).Clone(); baseGas = Draft.GasLimit; baseLiquid = Draft.LiquidLimit; BaseRevision = state.Revision; Stale = false; ResetText(); PriorityText = state.InputPriority.ToString(CultureInfo.CurrentCulture); }
        public bool AdvancedDirty => Draft.GasLimit != baseGas || Draft.LiquidLimit != baseLiquid;
        public bool ObserveAdvanced(BuildingState state)
        {
            if (BaseRevision == state.Revision) return false;
            var live = state.Pending ?? state.Applied;
            bool changed = live.GasLimit != baseGas || live.LiquidLimit != baseLiquid;
            bool invalid = Draft.GasLimit.HasValue && !MembraneSettings.TryLimit(GasText, CultureInfo.CurrentCulture, out _) || Draft.LiquidLimit.HasValue && !MembraneSettings.TryLimit(LiquidText, CultureInfo.CurrentCulture, out _);
            if (changed && !AdvancedDirty && !invalid) { string priorityText = PriorityText; Reset(state); PriorityText = priorityText; return true; }
            if (changed) Stale = true;
            Draft.Active = live.Active; Draft.Direction = live.Direction; Draft.Allowed = new HashSet<string>(live.Allowed, StringComparer.Ordinal);
            BaseRevision = state.Revision;
            return false;
        }
        public void ResetText() { GasText = (Draft.GasLimit ?? 0).ToString(CultureInfo.CurrentCulture); LiquidText = (Draft.LiquidLimit ?? 0).ToString(CultureInfo.CurrentCulture); }
        public void Observe(BuildingState state)
        {
            if (BaseRevision == state.Revision) return;
            // A submitted configuration can be waiting or finish draining
            // without being an external conflict. Acknowledge matching values
            // without overwriting raw text (including invalid input).
            if (Draft.Equivalent(state.Pending ?? state.Applied)) { BaseRevision = state.Revision; Stale = false; return; }
            Stale = true; // Preserve typed input until the player explicitly reloads.
        }
        public bool Dirty(BuildingState state) => !Draft.Equivalent(state.Applied);
    }
}
