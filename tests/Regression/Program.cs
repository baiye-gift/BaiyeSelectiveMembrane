using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Baiye.SelectiveMembrane.Core;

class Program
{
    static int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static void Near(double a, double b, string message) => Check(Math.Abs(a - b) < 1e-10, message + $" ({a} != {b})");
    static TransferLedger Stock(double mass = .2, string element = "Hydrogen", MediumPhase phase = MediumPhase.Gas, int germs = 100)
    {
        var b = new TransferLedger(); var id = b.BeginIntake(element, phase, mass, 10, 12, 1); b.AcceptIntake(id, mass, 300, 1, germs); return b;
    }
    static CellSample Cell(int cell, double mass, string element = "Hydrogen", MediumPhase phase = MediumPhase.Gas, int y = 1, int world = 0, bool solid = false) => new CellSample { Cell = cell, Mass = mass, Element = element, Phase = phase, Y = y, World = world, Valid = true, Solid = solid };
    static BuildingState Settings(bool active = false) { var state = new BuildingState(); state.Applied.Active = active; state.Applied.Allowed.UnionWith(new[] { "Hydrogen", "Water" }); return state; }
    static WorkPlan Plan(TransferLedger stock, BuildingState state, CellSample a, CellSample b) => TransferPolicy.Plan(stock, state, a, b, .2, id => id == a.Cell ? a : b);
    static void Main()
    {
        Test("an incomplete hydrogen cache rejects water", () => {
            var b = new TransferLedger();
            long id = b.BeginIntake("Hydrogen", MediumPhase.Gas, .2, 10, 12, 255);
            b.AcceptIntake(id, .2, 300, 255, 0);
            Check(b.BeginIntake("Water", MediumPhase.Liquid, .2, 10, 12, 255) == 0, "mixed phases admitted");
            Check(b.Capacity == .5 && b.Mass == .2, "gas lock lost");
        });
        Test("an empty cache with a reserved intake stays element locked", () => {
            var b = new TransferLedger(); long id = b.BeginIntake("Hydrogen", MediumPhase.Gas, .2, 10, 12, 255);
            Check(id != 0 && b.Mass == 0 && b.Occupied == .2, "reservation missing");
            Check(b.BeginIntake("Water", MediumPhase.Liquid, .1, 10, 12, 255) == 0, "in-flight lock lost");
        });
        Test("failed intake releases its reservation without inventing stock", () => {
            var b = new TransferLedger(); long id = b.BeginIntake("Hydrogen", MediumPhase.Gas, .2, 10, 12, 255);
            b.AcceptIntake(id, 0, 0, 255, 0); Check(b.Empty && b.Element == null, "failed consume kept phantom stock");
        });
        Test("partial consumption records actual amount and metadata", () => {
            var b = new TransferLedger(); long id = b.BeginIntake("Water", MediumPhase.Liquid, .4, 10, 12, 255);
            b.AcceptIntake(id, .05, 325, 2, 19); Check(b.Mass == .05 && b.Parcels[0].Germs == 19 && b.Parcels[0].Temperature == 325, "requested amount or snapshot metadata used");
        });
        Test("duplicate intake callbacks cannot duplicate matter", () => {
            var b = new TransferLedger(); long id = b.BeginIntake("Hydrogen", MediumPhase.Gas, .2, 10, 12, 255);
            b.AcceptIntake(id, .1, 300, 255, 0); Check(!b.AcceptIntake(id, .1, 300, 255, 0) && b.Mass == .1, "duplicate admitted");
        });
        Test("emission retains occupied capacity until confirmed", () => {
            var b = Stock(.5); var e = b.BeginEmit(.2); Check(b.Occupied == .5, "space freed early");
            Check(b.BeginEmit(.1) == null && b.BeginIntake("Hydrogen", MediumPhase.Gas, .1, 10, 12, 255) == 0, "double emit or overflow");
            b.FinishEmit(e.Id, true); Near(b.Mass, .3, "confirmed emission not deducted");
        });
        Test("failed emission retains exact heat and germs", () => {
            var b = Stock(.2); var e = b.BeginEmit(.07); b.FinishEmit(e.Id, false);
            Check(b.Mass == .2 && b.Parcels[0].Temperature == 300 && b.Parcels[0].Germs == 100, "failed output lost contents");
        });
        Test("duplicate output and old lifecycle IDs cannot settle a new request", () => {
            var b = Stock(); var e = b.BeginEmit(.05); b.FinishEmit(e.Id, true); var next = b.BeginEmit(.05);
            Check(!b.FinishEmit(e.Id, true) && b.Emission.Id == next.Id, "old ID settled new output");
        });
        Test("parallel intake and output remain within one locked gas capacity", () => {
            var b = Stock(.3); var e = b.BeginEmit(.1); var id = b.BeginIntake("Hydrogen", MediumPhase.Gas, .2, 10, 12, 255);
            Check(id != 0 && b.Occupied == .5, "parallel accounting wrong"); b.AcceptIntake(id, .2, 350, 255, 0); b.FinishEmit(e.Id, true); Near(b.Mass, .4, "parallel settlement wrong");
        });
        Test("fractional germ rounding preserves the entire original count", () => {
            var b = Stock(.2, germs: 7); int delivered = 0;
            while (!b.Empty) { var e = b.BeginEmit(.03); delivered += e.Germs; b.FinishEmit(e.Id, true); }
            Check(delivered == 7, "germ remainder discarded");
        });
        Test("different disease species remain separate physical parcels", () => {
            var b = Stock(.2, germs: 7); var id = b.BeginIntake("Hydrogen", MediumPhase.Gas, .1, 10, 12, 2); b.AcceptIntake(id, .1, 400, 2, 13);
            var e = b.BeginEmit(1); Check(e.Disease == 1 && e.Germs == 7 && e.Temperature == 300, "diseases merged"); b.FinishEmit(e.Id, true);
            e = b.BeginEmit(1); Check(e.Disease == 2 && e.Germs == 13 && e.Temperature == 400, "second parcel altered");
        });
        Test("tiny physical tails are never deleted by epsilon", () => {
            var b = Stock(1e-12); var e = b.BeginEmit(3e-13); b.FinishEmit(e.Id, true); Check(b.Mass > 0 && !b.Empty, "tiny remainder lost");
        });
        Test("saving refuses unresolved native transactions", () => {
            var b = Stock(); b.BeginEmit(.1); bool refused = false; try { b.ValidateForSave(); } catch (InvalidOperationException) { refused = true; } Check(refused, "unsafe snapshot permitted");
        });
        Test("gas and liquid use independent locked capacities", () => { Check(Stock(1, "Water", MediumPhase.Liquid).Capacity == 2 && Stock().Capacity == .5, "capacity wrong"); });
        Test("passive flow caps at half the available difference", () => {
            var p = Plan(new TransferLedger(), Settings(), Cell(10, .06), Cell(12, .04)); Near(p.Intake, .01, "equalization overshot"); Check(p.Watts == 0, "passive charged");
        });
        Test("passive flow cannot treat foreign trace gas as vacuum", () => {
            Check(!Plan(new TransferLedger(), Settings(), Cell(10, 1), Cell(12, 1e-12, "Oxygen")).HasWork, "trace overwritten");
        });
        Test("passive liquid cannot pump upward", () => {
            Check(!Plan(new TransferLedger(), Settings(), Cell(10, 100, "Water", MediumPhase.Liquid, 1), Cell(12, 0, "Vacuum", y: 3)).HasWork, "free upward pumping");
        });
        Test("passive liquid can flow downward", () => {
            var p = Plan(new TransferLedger(), Settings(), Cell(10, 100, "Water", MediumPhase.Liquid, 3), Cell(12, 0, "Vacuum", y: 1)); Near(p.Intake, 2.0 / 3 * .2, "downward flow blocked");
        });
        Test("active mode permits upward liquid flow with 240W demand", () => {
            var p = Plan(new TransferLedger(), Settings(true), Cell(10, 100, "Water", MediumPhase.Liquid, 1), Cell(12, 0, "Vacuum", y: 3)); Check(p.Intake == .4 && p.Watts == 240, "active plan wrong");
        });
        Test("active gas prefills a blocked outlet only to gas capacity", () => {
            var p = Plan(Stock(.5), Settings(true), Cell(10, 2), Cell(12, 20, "Water", MediumPhase.Liquid)); Check(!p.HasWork && p.Watts == 0, "full blocked cache draws power");
            p = Plan(Stock(.2), Settings(true), Cell(10, 2), Cell(12, 20, "Water", MediumPhase.Liquid)); Check(p.Intake == .1 && p.Watts == 120 && p.Output == 0, "bounded gas prefill wrong");
        });
        Test("liquid source cannot change existing gas cache power", () => {
            var p = Plan(Stock(), Settings(true), Cell(10, 20, "Water", MediumPhase.Liquid), Cell(12, 0, "Vacuum")); Check(p.Intake == 0 && p.Output > 0 && p.Watts == 120, "phase switched under stock");
        });
        Test("foreign media on both sides retain stock at zero demand", () => {
            var b = Stock(); var p = Plan(b, Settings(true), Cell(10, 20, "Water", MediumPhase.Liquid), Cell(12, 20, "Water", MediumPhase.Liquid)); Check(!p.HasWork && b.Mass == .2, "blocked stock changed");
        });
        Test("world boundaries and solid recipients never transfer", () => {
            Check(!Plan(new TransferLedger(), Settings(true), Cell(10, 1), Cell(12, 0, "Vacuum", world: 1)).HasWork, "cross-world");
            Check(!Plan(new TransferLedger(), Settings(true), Cell(10, 1), Cell(12, 0, "Vacuum", solid: true)).HasWork, "solid output");
        });
        Test("zero target limit is distinct from unlimited", () => {
            var state = Settings(true); state.Applied.GasLimit = 0; Check(!Plan(new TransferLedger(), state, Cell(10, 1), Cell(12, 0, "Vacuum")).HasWork, "zero ignored");
            state.Applied.GasLimit = null; Check(Plan(new TransferLedger(), state, Cell(10, 1), Cell(12, 0, "Vacuum")).HasWork, "unlimited blocked");
        });
        Test("editing configuration keeps the old route until drained", () => {
            var b = Stock(); var state = Settings(true); var next = state.Applied.Clone(); next.Direction = -1;
            Check(state.Request(next, new[] { b }) && state.Pending != null && state.Applied.Direction == 1, "old config replaced early");
            var p = Plan(b, state, Cell(10, 2), Cell(12, 0, "Vacuum")); Check(p.Intake == 0 && p.Target == 12 && p.Output > 0, "drain route replaced");
            var e = b.BeginEmit(b.Mass); b.FinishEmit(e.Id, true); Check(state.Settle(new[] { b }) && state.Applied.Direction == -1, "pending never committed");
        });
        Test("adding allowed media applies immediately without disturbing inventory", () => {
            var state = Settings(true); var next = state.Applied.Clone(); next.Allowed.Add("Oxygen"); var b = Stock(); state.Request(next, new[] { b });
            Check(state.Pending == null && state.Applied.Allowed.Contains("Oxygen") && b.Mass == .2, "safe addition unnecessarily drained");
        });
        Test("pause stops normal and configuration-draining work", () => {
            var state = Settings(true); state.Paused = true; var next = state.Applied.Clone(); next.Active = false; var b = Stock(); state.Request(next, new[] { b });
            Check(!Plan(b, state, Cell(10, 1), Cell(12, 0, "Vacuum")).HasWork, "paused drain ran");
        });
        Test("demolition permits zero-power return to original source while paused", () => {
            var state = Settings(true); state.Paused = state.Demolishing = true; var p = Plan(Stock(), state, Cell(10, 1), Cell(12, 0, "Vacuum"));
            Check(p.Output == .2 && p.Target == 10 && p.Intake == 0 && p.Watts == 0, "safe demolition return wrong");
        });
        Test("canceling pending settings cannot reverse delivered matter", () => {
            var b = Stock(); var state = Settings(); var next = state.Applied.Clone(); next.Direction = -1; state.Request(next, new[] { b }); var e = b.BeginEmit(.1); b.FinishEmit(e.Id, true); state.CancelPending();
            Check(state.Pending == null && state.Applied.Direction == 1 && b.Mass == .1, "cancel rolled back stock");
        });
        Test("drafts are independent and become stale on external copy", () => {
            var state = Settings(); var edit = new EditSession(state); edit.Draft.Active = true; Check(!state.Applied.Active, "draft mutated applied");
            var next = state.Applied.Clone(); next.Allowed.Add("Oxygen"); state.Request(next, Array.Empty<TransferLedger>()); edit.Observe(state); Check(edit.Stale && edit.Draft.Active && !edit.Draft.Allowed.Contains("Oxygen"), "input overwritten");
        });
        Test("numeric input rejects negatives NaN infinity and blank", () => {
            foreach (var text in new[] { "-1", "NaN", "Infinity", "1e999", "", "junk" }) Check(!MembraneSettings.TryLimit(text, CultureInfo.InvariantCulture, out _), "invalid accepted: " + text);
            Check(MembraneSettings.TryLimit("0", CultureInfo.InvariantCulture, out double zero) && zero == 0, "zero rejected");
            Check(MembraneSettings.TryLimit("1,25", CultureInfo.GetCultureInfo("fr-FR"), out double value) && value == 1.25, "culture ignored");
        });
        Test("partial payment gives every work unit the same fraction", () => {
            Near(NetworkPolicy.Fraction(12, 120, .2), .5, "half payment wrong"); Near(NetworkPolicy.Fraction(0, 0, .2), 0, "idle division"); Near(NetworkPolicy.Fraction(100, 120, .2), 1, "overbudget");
        });
        Test("multi-cell doors join adjacent tiles once, diagonals remain separate", () => {
            var nodes = new[] { new NetworkNode { Id = 1, World = 0, Cells = new[] { 22, 32 } }, new NetworkNode { Id = 2, World = 0, Cells = new[] { 33 } }, new NetworkNode { Id = 3, World = 0, Cells = new[] { 44 } } };
            var components = NetworkPolicy.Components(nodes, 10); Check(components.Count == 2 && components[0].Length == 2, "door duplicated or diagonal joined");
        });
        Test("topology cannot wrap rows or connect worlds", () => {
            var nodes = new[] { new NetworkNode { Id = 1, World = 0, Cells = new[] { 29 } }, new NetworkNode { Id = 2, World = 0, Cells = new[] { 30 } }, new NetworkNode { Id = 3, World = 1, Cells = new[] { 39 } } };
            Check(NetworkPolicy.Components(nodes, 10).Count == 3, "invalid adjacency");
        });
        Test("inputs deduplicate real circuits with deterministic priority", () => {
            var inputs = new[] { new InputCandidate { Cell = 30, Circuit = 1, Priority = 5 }, new InputCandidate { Cell = 20, Circuit = 1, Priority = 5 }, new InputCandidate { Cell = 40, Circuit = 2, Priority = 9 }, new InputCandidate { Cell = 10, Circuit = ushort.MaxValue, Priority = 9 } };
            var ranked = NetworkPolicy.Deduplicate(inputs); Check(ranked.Length == 2 && ranked[0].Cell == 40 && ranked[1].Cell == 20, "election wrong");
        });
        Test("main shortage switches after two seconds and idle does not fail", () => {
            var e = new SourceElection(); var ranked = new[] { new InputCandidate { Cell = 10 }, new InputCandidate { Cell = 12 } };
            e.Choose(ranked, 0, 0, false); for (int i = 0; i < 20; i++) e.Choose(ranked, 0, .2, false); Check(e.CurrentCell == 10, "idle falsely failed");
            e.Choose(ranked, 0, 1, true); Check(e.CurrentCell == 10, "failed too early"); e.Choose(ranked, 0, 1, true); Check(e.CurrentCell == 12, "backup never selected");
        });
        Test("failed primary comeback trial restores backup and cools down", () => {
            var e = new SourceElection(); var ranked = new[] { new InputCandidate { Cell = 10 }, new InputCandidate { Cell = 12 } };
            e.Choose(ranked, 0, 0, false); e.Choose(ranked, 0, 2, true); e.Choose(ranked, 1, 5, true); Check(e.CurrentCell == 10, "trial not started"); e.Choose(ranked, 0, .2, true); Check(e.CurrentCell == 12, "failed trial did not revert");
        });
        Test("random transfer failures conserve mass heat and all germs", () => {
            var random = new Random(182); var b = new TransferLedger(); double consumed = 0, heat = 0, emitted = 0, emittedHeat = 0; int germs = 0, emittedGerms = 0;
            for (int i = 0; i < 3000; i++)
            {
                double amount = Math.Min(.03, Math.Max(0, .5 - b.Occupied));
                if (amount > 0) { var id = b.BeginIntake("Hydrogen", MediumPhase.Gas, amount, 10, 12, 1); double actual = amount * random.NextDouble(); double temp = 270 + random.Next(50); int count = random.Next(20); b.AcceptIntake(id, actual, temp, 1, count); consumed += actual; heat += actual * temp; germs += count; }
                var ticket = b.BeginEmit(.03 * random.NextDouble());
                if (ticket != null) { bool success = random.Next(4) != 0; if (success) { emitted += ticket.Mass; emittedHeat += ticket.Mass * ticket.Temperature; emittedGerms += ticket.Germs; } b.FinishEmit(ticket.Id, success); }
            }
            Near(consumed, emitted + b.Mass, "mass not conserved"); Check(Math.Abs(heat - emittedHeat - b.Parcels.Sum(p => p.Mass * p.Temperature)) < 1e-7, "heat not conserved"); Check(germs == emittedGerms + b.Parcels.Sum(p => p.Germs), "germs not conserved");
        });
        Test("passive shared-source transactions stay exclusive until delivered", () => {
            var claims = new PassiveClaims(); var first = Stock(.01); var second = new TransferLedger();
            Check(claims.Claim(first, 10, 12) && !claims.Claim(second, 10, 14), "shared-source passive overdraw admitted");
            var e = first.BeginEmit(.01); first.FinishEmit(e.Id, false); claims.Settle(first);
            Check(!claims.Claim(second, 10, 14), "failed emission prematurely released source");
            e = first.BeginEmit(.01); first.FinishEmit(e.Id, true); claims.Settle(first);
            Check(claims.Claim(second, 10, 14), "settled source permanently reserved");
        });
        Test("passive shared-target and reversed-route requests cannot overlap", () => {
            var claims = new PassiveClaims(); var first = Stock(); var second = new TransferLedger();
            claims.Claim(first, 10, 12); Check(!claims.Claim(second, 14, 12) && !claims.Claim(second, 12, 10), "related passive routes overlapped");
        });
        Test("failed empty intake releases both passive endpoint claims", () => {
            var claims = new PassiveClaims(); var first = new TransferLedger(); var second = new TransferLedger();
            claims.Claim(first, 10, 12); var id = first.BeginIntake("Hydrogen", MediumPhase.Gas, .01, 10, 12, 255); first.AcceptIntake(id, 0, 0, 255, 0); claims.Settle(first);
            Check(claims.Claim(second, 10, 12), "empty failed transaction blocked network");
        });
        Test("invalid raw numeric drafts survive unrelated edits and re-selection", () => {
            var state = Settings(); state.Applied.GasLimit = .5; var edit = new EditSession(state) { GasText = "wrong" };
            edit.Draft.LiquidLimit = 1; edit.Observe(state);
            Check(edit.GasText == "wrong" && !MembraneSettings.TryLimit(edit.GasText, CultureInfo.CurrentCulture, out _), "unrelated control overwrote invalid text");
            edit.Reset(state); Check(edit.GasText == .5.ToString(CultureInfo.CurrentCulture), "explicit undo did not restore valid value");
        });
        Test("visual neighbours cannot wrap rows or leave the grid", () => {
            Check(VisualPolicy.Mask(39, 20, 400, _ => true) == 13, "right edge wrapped");
            Check(VisualPolicy.Mask(0, 20, 400, _ => true) == 10, "corner escaped");
            Check(VisualPolicy.Mask(399, 20, 400, _ => true) == 5, "top corner escaped");
        });
        Test("visual adjacency only connects supplied completed members", () => {
            var cells = new HashSet<int> { 167, 169, 188 };
            Check(VisualPolicy.Mask(168, 20, 400, cells.Contains) == 11, "completed neighbours missing");
            cells.Remove(169);
            Check(VisualPolicy.Mask(168, 20, 400, cells.Contains) == 9, "removed neighbour still shown");
        });
        Test("rotated frames map world joins into local art coordinates", () => {
            Check(VisualPolicy.LocalMask(8, 1) == 1, "world north is not local west at R90");
            Check(VisualPolicy.LocalMask(1, 1) == 4, "world west is not local south at R90");
            for (int mask = 0; mask < 16; mask++) Check(VisualPolicy.LocalMask(mask, 4) == mask, "full turn changes neighbours");
        });
        Test("visual working signal distinguishes pause and no paid power", () => {
            var state = Settings(true);
            Check(VisualPolicy.Signal(state, true, false, 0) == VisualSignal.Waiting, "unpaid activity shown working");
            state.Paused = true;
            Check(VisualPolicy.Signal(state, true, true, 1) == VisualSignal.Paused, "paused activity shown working");
            state.Paused = false; state.Pending = state.Applied.Clone();
            Check(VisualPolicy.Signal(state, true, true, 1) == VisualSignal.Draining, "configuration drain lost");
        });
        Test("door display transitions do not depend on filtration pause", () => {
            Check(VisualPolicy.DoorClip(true, true) == "opening" && VisualPolicy.DoorClip(false, true) == "closing", "transition clip mismatch");
            Check(VisualPolicy.DoorClip(true, false) == "open" && VisualPolicy.DoorClip(false, false) == "closed", "stable clip mismatch");
        });
        Test("a newly selected draining building shows its pending configuration", () => {
            var state = Settings(); var pending = state.Applied.Clone(); pending.Active = true; pending.GasLimit = .75;
            state.Request(pending, new[] { Stock(.1) });
            var edit = new EditSession(state);
            Check(state.Pending != null && edit.Draft.Active && edit.Draft.GasLimit == .75, "saved pending configuration displayed as old applied values");
            Check(edit.GasText == .75.ToString(CultureInfo.CurrentCulture), "pending numeric text not loaded");
        });
        Test("settling the submitted draft does not report an external configuration conflict", () => {
            var state = Settings(); var unit = Stock(.1); var pending = state.Applied.Clone(); pending.Active = true;
            state.Request(pending, new[] { unit }); var edit = new EditSession(state); edit.Draft = pending.Clone(); edit.GasText = "wrong";
            var ticket = unit.BeginEmit(.1); unit.FinishEmit(ticket.Id, true); state.Settle(new[] { unit }); edit.Observe(state);
            Check(!edit.Stale && edit.BaseRevision == state.Revision, "own drain completion falsely invalidates the draft");
            Check(edit.GasText == "wrong", "revision acknowledgement erased raw input");
        });
        Test("an external draining configuration invalidates an existing old-value draft", () => {
            foreach (bool edited in new[] { false, true })
            {
                var state = Settings(); var edit = new EditSession(state) { GasText = "wrong" };
                if (edited) edit.Draft.Direction = -1;
                var external = state.Applied.Clone(); external.Active = true;
                state.Request(external, new[] { Stock(.1) }); edit.Observe(state);
                Check(state.Pending != null && edit.Stale && !edit.Draft.Active && edit.Draft.Direction == (edited ? -1 : 1), "external pending configuration falsely acknowledged or overwrote the draft");
                Check(edit.GasText == "wrong", "external pending configuration erased raw input");
                edit.Reset(state);
                Check(!edit.Stale && edit.Draft.Active && edit.Draft.Direction == 1, "explicit reload does not show pending values");
            }
        });
        Test("transport geometry distinguishes wall axes and rejects grid wrapping", () => {
            var horizontal = FlowGuidance.Endpoints(168, false, 20, 400); var vertical = FlowGuidance.Endpoints(168, true, 20, 400);
            Check(horizontal.Item1 == 167 && horizontal.Item2 == 169 && vertical.Item1 == 148 && vertical.Item2 == 188, "wall sides wrong");
            Check(FlowGuidance.Endpoints(39, false, 20, 400).Item2 == -1 && FlowGuidance.Endpoints(20, false, 20, 400).Item1 == -1, "row wrap");
            Check(FlowGuidance.Endpoints(0, true, 20, 400).Item1 == -1 && FlowGuidance.Endpoints(399, true, 20, 400).Item2 == -1, "border escaped");
        });
        Test("one top-wall tile transfers oxygen only across its vertical axis", () => {
            var state = Settings(); state.Applied.Allowed.Add("Oxygen");
            var oxygen = Cell(188, 1.252, "Oxygen", y: 9); var vacuum = Cell(148, 0, "Vacuum", y: 7);
            Check(Plan(new TransferLedger(), state, oxygen, vacuum).Intake > 0, "one rotated tile cannot pass oxygen");
            Check(!Plan(new TransferLedger(), state, Cell(167, 400, "Salt", solid: true), Cell(169, 400, "Salt", solid: true)).HasWork, "default axis passes neighboring solids");
            Check(FlowGuidance.Obstruction(false, true, true).Contains("旋转为上下"), "no actionable top-wall hint");
        });
        Test("quick choices commit independently of invalid and unsaved advanced limits", () => {
            var state = Settings(); state.Applied.GasLimit = .5; var edit = new EditSession(state); edit.Draft.GasLimit = 9; edit.GasText = "wrong";
            Check(SettingsCommands.Quick(state, Array.Empty<TransferLedger>(), cfg => cfg.Allowed.Add("Oxygen")), "quick choice refused"); edit.ObserveAdvanced(state);
            Check(state.Applied.Allowed.Contains("Oxygen") && state.Applied.GasLimit == .5, "unsaved upper limit submitted");
            Check(edit.Draft.GasLimit == 9 && edit.GasText == "wrong" && !edit.Stale && edit.Draft.Allowed.Contains("Oxygen"), "advanced input lost on quick choice");
        });
        Test("quick mode drains old stock and acknowledges settlement without erasing advanced input", () => {
            var state = Settings(); var unit = Stock(.1); var edit = new EditSession(state); edit.Draft.LiquidLimit = 2; edit.LiquidText = "2";
            Check(SettingsCommands.Quick(state, new[] { unit }, cfg => cfg.Active = true), "mode refused"); edit.ObserveAdvanced(state);
            Check(state.Pending.Active && !state.Applied.Active && !edit.Stale, "old path bypassed");
            Check(!SettingsCommands.Quick(state, new[] { unit }, cfg => cfg.Direction = -1), "second pending admitted");
            var ticket = unit.BeginEmit(.1); unit.FinishEmit(ticket.Id, true); state.Settle(new[] { unit }); edit.ObserveAdvanced(state);
            Check(!edit.Stale && edit.Draft.Active && edit.Draft.LiquidLimit == 2 && edit.LiquidText == "2", "own settlement conflicts with advanced edits");
        });
        Test("external basic changes merge but external limits preserve a conflicted local input", () => {
            var state = Settings(); var edit = new EditSession(state); edit.Draft.GasLimit = 7; edit.GasText = "7";
            SettingsCommands.Quick(state, Array.Empty<TransferLedger>(), cfg => cfg.Direction = -1); edit.ObserveAdvanced(state);
            Check(!edit.Stale && edit.Draft.Direction == -1, "external basic setting conflicts with limits");
            SettingsCommands.Quick(state, Array.Empty<TransferLedger>(), cfg => cfg.GasLimit = 2); edit.ObserveAdvanced(state);
            Check(edit.Stale && edit.Draft.GasLimit == 7 && edit.GasText == "7", "external limit erased edited value");
            edit.Reset(state); Check(!edit.Stale && edit.Draft.GasLimit == 2, "explicit reload wrong");
        });
        Test("clean advanced fields follow external saved limits and demolition rejects quick changes", () => {
            var state = Settings(); var edit = new EditSession(state) { PriorityText = "9" };
            SettingsCommands.Quick(state, Array.Empty<TransferLedger>(), cfg => cfg.GasLimit = 2); Check(edit.ObserveAdvanced(state) && edit.GasText == "2", "clean limits do not refresh");
            Check(edit.PriorityText == "9", "external limits reset the independent priority input");
            state.Demolishing = true; int revision = state.Revision;
            Check(!SettingsCommands.Quick(state, Array.Empty<TransferLedger>(), cfg => cfg.Allowed.Clear()) && revision == state.Revision, "demolition settings changed");
        });
        Test("waiting diagnostics distinguish foreign medium limit gravity and balance", () => {
            var state = Settings(); var a = Cell(10, 1); var b = Cell(12, .5, "Oxygen"); var empty = new TransferLedger();
            Check(FlowGuidance.Waiting(empty, state, a, b, 0, _ => b).Contains("其他介质"), "foreign outlet mistaken for working");
            b = Cell(12, .5); state.Applied.GasLimit = .4;
            Check(FlowGuidance.Waiting(empty, state, a, b, 0, _ => b).Contains("目标上限"), "zero headroom unexplained");
            state.Applied.GasLimit = null; b.Mass = a.Mass;
            Check(FlowGuidance.Waiting(empty, state, a, b, 0, _ => b).Contains("平衡"), "balanced state unexplained");
            a = Cell(10, 1, "Water", MediumPhase.Liquid, y: 1); b = Cell(12, 0, "Vacuum", y: 2);
            Check(FlowGuidance.Waiting(empty, state, a, b, 0, _ => b).Contains("向上输液"), "gravity unexplained");
        });
        Console.WriteLine($"{passed} regression scenarios passed");
    }
}
