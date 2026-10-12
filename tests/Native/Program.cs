using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using Baiye.SelectiveMembrane;
using Baiye.SelectiveMembrane.Core;
using HarmonyLib;
using Newtonsoft.Json;

static class Program
{
    static int passed;
    static void Main(string[] args)
    {
        AppContext.SetData("MonoMod.DMDType", "cecil");
        string managed = args.Length == 0 ? @"D:\Steam\steamapps\common\OxygenNotIncluded\OxygenNotIncluded_Data\Managed" : Path.GetFullPath(args[0]);
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => { string file = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll"); return File.Exists(file) ? Assembly.LoadFrom(file) : null; };
        try { Run(); } catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static void Test(string name, System.Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    static List<MethodInfo> Calls(MethodBase method) => PatchProcessor.GetOriginalInstructions(method).Select(i => i.operand).OfType<MethodInfo>().ToList();
    [MethodImpl(MethodImplOptions.NoInlining)] static void Run()
    {
        // The game's .NET Framework Newtonsoft CAS probe is unsupported in
        // this .NET 8 host. Its shipped late-bound reflection backend uses the
        // same contracts without changing production/game serializer settings.
        var reflector = typeof(JsonConvert).Assembly.GetType("Newtonsoft.Json.Serialization.JsonTypeReflector");
        AccessTools.Field(reflector, "_dynamicCodeGeneration").SetValue(null, false);
        Test("all Harmony patches bind to the installed game build", () => {
            var harmony = new Harmony("BaiyeMembrane.Native.ContractTests"); harmony.PatchAll(typeof(MembraneMod).Assembly);
            var methods = harmony.GetPatchedMethods().ToArray(); Assert(methods.Length >= 12, "patch set missing"); harmony.UnpatchAll(harmony.Id);
        });
        Test("payment patch targets both original generator and battery debit paths", () => {
            foreach (var name in new[] { "PowerFromGenerator", "PowerFromBatteries" })
            {
                var method = AccessTools.Method(typeof(CircuitManager), name); Assert(method.ReturnType == typeof(float), "remaining joules contract changed");
                Assert(method.GetParameters().Any(p => p.Name == "c" && p.ParameterType == typeof(IEnergyConsumer)), "consumer binding changed");
            }
            var input = new MembranePowerInput(null, 8) { Demand = 120 };
            var payment = typeof(MembraneMod).Assembly.GetType("Baiye.SelectiveMembrane.MembraneActualPayment");
            AccessTools.Method(payment, "Postfix").Invoke(null, new object[] { input, 12f, 24f });
            AccessTools.Method(payment, "Postfix").Invoke(null, new object[] { input, 8f, 12f });
            Assert(input.Paid == 16, "partial payments not accumulated");
            input.SetConnectionStatus(CircuitManager.ConnectionStatus.Unpowered); Assert(input.Paid == 16 && !input.IsPowered, "unpowered discarded partial payment");
        });
        Test("native consumption has exact-cell radius and a real result callback", () => {
            Assert(typeof(Sim.MassConsumedCallback).GetField("mass").FieldType == typeof(float), "actual amount callback missing");
            var method = typeof(SimMessages).GetMethods().Single(m => m.Name == "ConsumeMass" && m.GetParameters().Length == 5);
            Assert(method.GetParameters()[3].ParameterType == typeof(byte), "radius signature changed");
            Assert(Calls(AccessTools.Method(typeof(NativeTransfer), "Intake")).Any(m => m == method), "production not using native callback consumption");
        });
        Test("emission contract exposes the original success flag and handle manager", () => {
            Assert(typeof(Sim.MassEmittedCallback).GetField("suceeded")?.FieldType == typeof(byte), "native success flag changed");
            Assert(AccessTools.Field(typeof(Game), "massEmitCallbackManager") != null, "callback manager missing");
            Assert(Calls(AccessTools.Method(typeof(NativeTransfer), "Emit")).Any(m => m.Name == "EmitMass"), "production bypasses safe emitter");
        });
        Test("missing-element preflight precedes creating an emission ticket", () => {
            // ElementLoader's initializer needs Unity Application. Exercise the
            // production lookup on loaded-list input and inspect native adapter
            // ordering without pretending the Unity loader ran in this host.
            Assert(NativeTransfer.ResolveLoaded(Array.Empty<Element>(), "MissingOtherModElement") == null, "unavailable element invented");
            var calls = Calls(AccessTools.Method(typeof(NativeTransfer), "Emit"));
            Assert(calls.FindIndex(m => m.Name == "Resolve") < calls.FindIndex(m => m.Name == "BeginEmit"), "missing element can poison native reservations");
        });
        Test("float conversion never reserves more than native requested mass", () => {
            foreach (double value in new[] { .1, .3, .5 - .49999999, 1e-12, .00000001 })
            { float result = NativeTransfer.NativeAmount(value); Assert(result > 0 && result <= value, "float rounded beyond reservation"); }
        });
        Test("persistent JSON round trip retains all parcels pending configuration and order intent", () => {
            var unit = new TransferLedger(); var id = unit.BeginIntake("Hydrogen", MediumPhase.Gas, .2, 10, 12, 1); unit.AcceptIntake(id, .17, 347, 1, 11);
            var state = new BuildingState { Demolishing = true, Paused = true, InputPriority = 9 };
            state.Applied.Allowed.Add("Hydrogen"); var next = state.Applied.Clone(); next.Active = true; state.Request(next, new[] { unit });
            var saved = JsonConvert.DeserializeObject<SavedBuilding>(JsonConvert.SerializeObject(new SavedBuilding { State = state, Units = new[] { unit } }));
            saved.Units[0].ValidateForSave(); Assert(saved.Units[0].Mass == .17 && saved.Units[0].Parcels[0].Temperature == 347 && saved.Units[0].Parcels[0].Germs == 11, "matter changed");
            Assert(saved.State.Pending.Active && saved.State.Demolishing && saved.State.Paused && saved.State.InputPriority == 9, "intent lost");
            Assert(!saved.Units[0].InFlight && saved.Units[0].Sequence == unit.Sequence, "native handle replayed");
        });
        Test("building and world recovery use actual game save annotations", () => {
            foreach (var type in new[] { typeof(MembraneBuilding), typeof(MembraneRecovery) })
            {
                Assert(AccessTools.Field(type, "snapshot").IsDefined(typeof(KSerialization.Serialize), false), "snapshot not native serialized");
                Assert(AccessTools.Method(type, "Serialize").IsDefined(typeof(System.Runtime.Serialization.OnSerializingAttribute), false), "native save callback missing");
            }
            Assert(AccessTools.Method(typeof(SaveLoader), "Save", new[] { typeof(BinaryWriter) }) != null && AccessTools.Method(typeof(Game), "UnsafeSim200ms") != null, "save barrier binding changed");
        });
        Test("door prefab has one Sim owner and custom nav rather than vanilla opening", () => {
            var complete = Calls(AccessTools.Method(typeof(MembraneDoorConfig), "DoPostConfigureComplete"));
            var types = complete.Where(m => m.IsGenericMethod).SelectMany(m => m.GetGenericArguments()).ToArray();
            Assert(types.Contains(typeof(MembraneDoor)) && types.Contains(typeof(AccessControl)) && !types.Contains(typeof(Door)), "vanilla Door changes solid cells");
            var configure = Calls(AccessTools.Method(typeof(MembraneTileConfig), "Configure"));
            Assert(configure.Any(m => m.IsGenericMethod && m.GetGenericArguments().Contains(typeof(SimCellOccupier))), "solid owner missing");
            Assert(!Calls(AccessTools.Method(typeof(MembraneDoor), "Open")).Any(m => m.DeclaringType == typeof(SimMessages)), "animation changes Sim");
        });
        Test("native navigation accepts solid passable cells and blocks locked cells", () => {
            Grid.WidthInCells = 20; Grid.HeightInCells = 20; Grid.CellCount = 400;
            Grid.BuildMasks = new Grid.BuildFlags[400]; Grid.NavMasks = new Grid.NavFlags[400]; Grid.NavValidatorMasks = new Grid.NavValidatorFlags[400];
            var nav = new NavTable(400, "MembraneContract"); int cell = 168; nav.SetValid(cell + 1, NavType.Floor, true);
            var t = new NavGrid.Transition(NavType.Floor, NavType.Floor, 1, 0, NavAxis.NA, false, false, false, 5, "", new[] { new CellOffset(1, 0) }, Array.Empty<CellOffset>(), Array.Empty<NavOffset>(), Array.Empty<NavOffset>());
            Grid.BuildMasks[cell + 1] = Grid.BuildFlags.Solid | Grid.BuildFlags.DupePassable;
            Assert(t.IsValid(cell, nav) == cell + 1, "native solid/passable handshake invalid");
            Grid.BuildMasks[cell + 1] = Grid.BuildFlags.Solid | Grid.BuildFlags.DupeImpassable;
            Assert(t.IsValid(cell, nav) == Grid.InvalidCell, "native lock allows entry");
        });
        Test("loaded occupants rebuild exit eligibility before imposing the door barrier", () => {
            var refresh = Calls(AccessTools.Method(typeof(MembraneDoor), "RefreshNavigation"));
            Assert(refresh.FindIndex(m => m.Name == "RecoverOccupant") < refresh.FindIndex(m => m.Name == "SetDupePassableSolid"), "load can impose a barrier before restoring occupant exit");
            var hook = typeof(MembraneMod).Assembly.GetType("Baiye.SelectiveMembrane.MembraneLoadedOccupant");
            Assert(Calls(AccessTools.Method(hook, "Postfix")).Any(m => m.Name == "RecoverOccupant"), "late-spawned loaded minions miss exit restoration");
        });
        Test("settings registration retains original panels and is idempotent", () => {
            var refs = new List<DetailsScreen.SideScreenRef> { new DetailsScreen.SideScreenRef { name = "original" } };
            var screen = (MembraneSideScreen)RuntimeHelpers.GetUninitializedObject(typeof(MembraneSideScreen));
            Assert(MembraneSideScreenRegistration.Add(refs, screen) && !MembraneSideScreenRegistration.Add(refs, screen), "duplicate screen registered");
            Assert(refs.Count == 2 && refs[0].name == "original" && ReferenceEquals(refs[1].screenPrefab, screen) && refs[1].tab == DetailsScreen.SidescreenTabTypes.Config, "native panels changed");
        });
        Test("own art component replaces the vanilla grid atlas on completed tiles", () => {
            var complete = Calls(AccessTools.Method(typeof(MembraneTileConfig), "DoPostConfigureComplete"));
            var types = complete.Where(m => m.IsGenericMethod).SelectMany(m => m.GetGenericArguments()).ToArray();
            Assert(types.Contains(typeof(MembraneVisual)) && !types.Contains(typeof(KAnimGridTileVisualizer)), "stock grid renderer still owns our art");
            var create = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MembraneTileConfig), "Create"));
            var names = create.Select(i => i.operand).OfType<string>().ToArray();
            Assert(names.Contains(MembraneVisual.TileAnim) && names.Contains(MembraneVisual.DoorAnim), "own animation references missing");
        });
        Test("door display queues its terminal frame without touching the Sim", () => {
            var calls = Calls(AccessTools.Method(typeof(MembraneVisual), "SetPassage"));
            Assert(calls.Any(m => m.Name == "Play") && calls.Any(m => m.Name == "Queue"), "transition does not settle");
            Assert(!calls.Any(m => m.DeclaringType == typeof(SimMessages)), "visual state changes isolation");
            Assert(Calls(AccessTools.Method(typeof(MembraneDoor), "Open")).Any(m => m.Name == "SetPassage"), "door still bypasses own animation");
        });
        Test("display refresh uses existing native tint API and avoids topology polling", () => {
            Assert(AccessTools.Method(typeof(KBatchedAnimController), "SetSymbolTint", new[] { typeof(KAnimHashedString), typeof(UnityEngine.Color) }) != null, "native tint signature changed");
            var calls = Calls(AccessTools.Method(typeof(MembraneVisual), "Sim200ms"));
            Assert(!calls.Any(m => m.Name == "RefreshLocal" || m.Name == "NotifyNeighbors"), "display polls adjacency every tick");
            // Static adapter contract only: the call receives Travelers != 0,
            // rather than a constant true behind a nonzero-only branch.
            var tick = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MembraneVisual), "Sim200ms")).ToList();
            int passageCall = tick.FindIndex(i => i.operand is MethodInfo m && m.Name == "SetPassage");
            Assert(passageCall > 0 && tick[passageCall - 1].opcode == System.Reflection.Emit.OpCodes.Cgt_Un,
                "pruned travelers cannot reconcile the closed display");
        });
        Test("world teardown clears the visual member registry", () => {
            Assert(Calls(AccessTools.Method(typeof(MembraneNetworks), "Reset")).Any(m => m.DeclaringType == typeof(MembraneVisual) && m.Name == "Reset"), "visual members leak across worlds");
        });
        Test("hidden settings controls refresh without an active ancestor lookup", () => {
            // The real crash at IL_011e follows a zero-argument parent lookup
            // while DetailsScreen has not activated the panel. This is a
            // managed adapter check, not a claim to instantiate Unity UI.
            Assert(!Calls(AccessTools.Method(typeof(MembraneSideScreen), "RefreshView")).Any(m => m.Name == "GetComponentInParent"),
                "hidden direction/cancellation buttons still require an active ancestor");
            Assert(AccessTools.Field(typeof(MembraneSideScreen), "buttonRoots")?.FieldType == typeof(Dictionary<KButton, UnityEngine.GameObject>),
                "cloned layout roots are not retained independently of visibility");
        });
        Test("settings construction completes before callbacks can refresh missing controls", () => {
            var build = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MembraneSideScreen), "Build")).ToList();
            int initialized = build.FindLastIndex(i => i.opcode == System.Reflection.Emit.OpCodes.Stfld && i.operand is FieldInfo f && f.Name == "bulkApply");
            int ready = build.FindLastIndex(i => i.opcode == System.Reflection.Emit.OpCodes.Stfld && i.operand is FieldInfo f && f.Name == "built");
            Assert(initialized >= 0 && ready > initialized, "early built flag exposes half-built settings controls to callbacks");
        });
        Test("native panel deactivation closes overlay popups while retaining editing sessions", () => {
            var disable = AccessTools.DeclaredMethod(typeof(MembraneSideScreen), "OnCmpDisable");
            Assert(disable != null && Calls(disable).Any(m => m.Name == "ClosePopup") && Calls(disable).Any(m => m.Name == "FinishEditing"),
                "DetailsScreen direct SetActive(false) leaves an editable orphan overlay");
            Assert(!Calls(disable).Any(m => m.Name == "ClearTarget" || m.Name == "Clear"), "panel deactivation discards retained drafts");
            foreach (var method in new[] { "ClosePopup", "RebuildElementList" })
                Assert(Calls(AccessTools.Method(typeof(MembraneSideScreen), method)).Any(m => m.Name == "ForgetButton"), "transient button roots leak: " + method);
        });
        Test("cloned text input activates its original-game input owner", () => {
            var bind = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(MembraneSideScreen), "InputField")).ToList();
            Assert(bind.Any(i => i.opcode == System.Reflection.Emit.OpCodes.Stfld && i.operand is FieldInfo f && f.Name == "activateOnSpawn"),
                "text editing owner never joins the native screen input stack");
        });
        Test("native rotated door placements and simulation guidance agree", () => {
            Grid.WidthInCells = 20; Grid.CellCount = 400;
            var def = (BuildingDef)RuntimeHelpers.GetUninitializedObject(typeof(BuildingDef));
            def.PlacementOffsets = new[] { new CellOffset(0, 0), new CellOffset(0, 1) };
            var normal = MembraneFlowPreview.Placement(def, 168, Orientation.Neutral);
            var rotated = MembraneFlowPreview.Placement(def, 168, Orientation.R90);
            Assert(normal.SequenceEqual(new[] {168,188}) && rotated.SequenceEqual(new[] {168,169}), "door rotation origin differs from native placement");
            Assert(FlowGuidance.Endpoints(rotated[1], true, 20, 400).Item2 == 189, "second door unit points at wrong outlet");
            Assert(Calls(AccessTools.Method(typeof(MembraneBuilding), "Endpoints")).Any(m => m.DeclaringType == typeof(FlowGuidance)), "simulation axis differs from preview axis");
        });
        Test("preview hooks cover blueprints selected buildings and native hover instructions", () => {
            foreach (var type in new[] { typeof(MembraneTileConfig), typeof(MembraneDoorConfig) })
                foreach (var name in new[] { "DoPostConfigurePreview", "DoPostConfigureUnderConstruction", "DoPostConfigureComplete" })
                    Assert(Calls(AccessTools.Method(type, name)).Any(m => m.IsGenericMethod && m.GetGenericArguments().Contains(typeof(MembraneFlowPreview))), "missing visual guide: " + type.Name + "." + name);
            Assert(AccessTools.Property(typeof(KSelectable), "IsSelected") != null && AccessTools.Property(typeof(BuildTool), "GetBuildingOrientation") != null, "selection/rotation source changed");
            Assert(!Calls(AccessTools.Method(typeof(MembraneFlowPreview), "Update")).Any(m => m.DeclaringType == typeof(SimMessages)), "preview mutates Sim");
            Assert(Calls(AccessTools.Method(typeof(MembraneConstructionHint), "Postfix")).Any(m => m.Name == "GetActionString"), "rotation help ignores rebound key");
        });
        Test("confirmed element selection submits through the safe quick configuration path", () => {
            Assert(!Calls(AccessTools.Method(typeof(MembraneConstructionHint), "Postfix")).Any(m => m.Name == "BeginDrawing"), "flow card erases original construction instructions");
            var calls = Calls(AccessTools.Method(typeof(MembraneSideScreen), "ConfirmElements"));
            Assert(calls.Any(m => m.DeclaringType == typeof(SettingsCommands) && m.Name == "Quick"), "selection remains an unapplied draft");
            Assert(!calls.Any(m => m.Name == "CaptureLimitFields" || m.Name == "CommitDraft"), "choice submits unsaved numeric fields");
            Assert(AccessTools.Field(typeof(MembraneSideScreen), "popupRevision") != null, "stale chooser has no revision guard");
        });
        Console.WriteLine($"{passed} native managed contracts passed (no Unity/SimDLL execution)");
    }
}
