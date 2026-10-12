using System;

namespace Baiye.SelectiveMembrane.Core
{
    public struct CellSample
    {
        public int Cell, World, Y;
        public bool Valid, Solid;
        public string Element;
        public MediumPhase Phase;
        public double Mass;
        public bool Vacuum => Element == "Vacuum";
        public bool Accepts(string element) => Valid && !Solid && (Vacuum || Element == element);
    }
    public sealed class WorkPlan
    {
        public int Source = -1, Target = -1;
        public string Element;
        public MediumPhase Phase;
        public double Intake, Output, Watts;
        public bool Passive;
        public bool HasWork => Intake > 0 || Output > 0;
    }
    public static class TransferPolicy
    {
        public static double Rate(MediumPhase phase, bool active) => phase == MediumPhase.Gas ? (active ? .5 : 1.0 / 6) : (active ? 2 : 2.0 / 3);
        public static WorkPlan Plan(TransferLedger stock, BuildingState state, CellSample a, CellSample b, double dt, Func<int, CellSample> read)
        {
            var result = new WorkPlan();
            if (dt <= 0 || !TransferLedger.Finite(dt)) return result;
            bool demolition = state.Demolishing;
            if (state.Paused && !demolition) return result;
            if (!a.Valid || !b.Valid || a.World != b.World) return result;
            var cfg = state.Applied;
            if (!stock.Empty)
            {
                result.Element = stock.Element; result.Phase = stock.Phase;
                result.Source = stock.Source; result.Target = stock.Target;
                var target = read(result.Target);
                if (demolition)
                {
                    var source = read(stock.Source);
                    if (source.World == a.World && source.Accepts(stock.Element)) { target = source; result.Target = stock.Source; }
                }
                double cap = Rate(stock.Phase, cfg.Active) * dt;
                if (target.World == a.World && target.Accepts(stock.Element) && stock.Emission == null)
                    result.Output = Math.Min(stock.Mass, demolition ? stock.Mass : Math.Min(cap, Headroom(cfg, target, stock.Phase)));
                if (demolition || state.Pending != null || !cfg.Active) return Power(result, cfg.Active, demolition);
                var intake = read(stock.Source);
                if (intake.World == a.World && Eligible(intake, cfg) && intake.Element == stock.Element && intake.Phase == stock.Phase && stock.IntakeId == 0)
                    result.Intake = Math.Min(Math.Min(cap, intake.Mass), Math.Max(0, stock.Capacity - stock.Occupied));
                return Power(result, true, false);
            }
            if (demolition || state.Pending != null) return result;
            CellSample from, to;
            if (cfg.Active) { from = cfg.Direction == 1 ? a : b; to = cfg.Direction == 1 ? b : a; }
            else { from = a.Mass > b.Mass ? a : b; to = a.Mass > b.Mass ? b : a; }
            if (!Eligible(from, cfg) || to.Solid || !to.Valid) return result;
            if (!cfg.Active && (!to.Accepts(from.Element) || (from.Phase == MediumPhase.Liquid && to.Y > from.Y))) return result;
            result.Element = from.Element; result.Phase = from.Phase; result.Source = from.Cell; result.Target = to.Cell;
            double maximum = Rate(from.Phase, cfg.Active) * dt;
            if (!cfg.Active) maximum = Math.Min(maximum, Math.Max(0, (from.Mass - to.Mass) / 2));
            if (to.Accepts(from.Element)) maximum = Math.Min(maximum, Headroom(cfg, to, from.Phase));
            // Active prefetch is permitted only within this element's real capacity.
            result.Intake = Math.Min(Math.Min(maximum, from.Mass), from.Phase == MediumPhase.Gas ? .5 : 2);
            return Power(result, cfg.Active, false);
        }
        static bool Eligible(CellSample from, MembraneSettings cfg) => from.Valid && !from.Solid && !from.Vacuum && from.Mass > 0 && cfg.Allowed.Contains(from.Element);
        public static double Headroom(MembraneSettings cfg, CellSample target, MediumPhase phase)
        {
            double? limit = phase == MediumPhase.Gas ? cfg.GasLimit : cfg.LiquidLimit;
            return limit.HasValue ? Math.Max(0, limit.Value - target.Mass) : double.PositiveInfinity;
        }
        static WorkPlan Power(WorkPlan plan, bool active, bool demolition)
        {
            plan.Watts = plan.HasWork && active && !demolition ? (plan.Phase == MediumPhase.Gas ? 120 : 240) : 0;
            plan.Passive = !active && !demolition;
            return plan;
        }
    }
}
