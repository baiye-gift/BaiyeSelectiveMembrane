using HarmonyLib;
using KMod;
using UnityEngine;
using TUNING;

namespace Baiye.SelectiveMembrane
{
    public sealed class MembraneMod : UserMod2
    {
        public override void OnLoad(Harmony harmony) { base.OnLoad(harmony); MembraneStrings.Register(); }
    }
    internal static class MembraneStrings
    {
        public static string Text(string zh, string en) => Localization.GetLocale()?.Code?.StartsWith("zh") != false ? zh : en;
        public static void Register()
        {
            Add(MembraneTileConfig.ID, Text("选择性分子筛砖", "Selective membrane tile"));
            Add(MembraneDoorConfig.ID, Text("选择性分子筛门", "Selective membrane door"));
        }
        static void Add(string id, string name)
        {
            string root = "STRINGS.BUILDINGS.PREFABS." + id.ToUpperInvariant();
            Strings.Add(root + ".NAME", name);
            Strings.Add(root + ".DESC", Text("精密复合分子筛膜持续隔离气液，选择性输运指定介质。", "A precision composite membrane that maintains isolation while selectively transferring allowed media."));
            Strings.Add(root + ".EFFECT", Text("默认白名单为空。被动缓慢平衡；主动按实际供电比例定向输运。相邻分子筛组成供电网络。", "Starts with an empty allowlist. Passive equalization or powered directional transfer. Adjacent membranes share a power budget."));
        }
    }
    [HarmonyPatch(typeof(Db), nameof(Db.Initialize))]
    internal static class MembraneRegistration
    {
        static void Postfix()
        {
            foreach (string id in new[] { MembraneTileConfig.ID, MembraneDoorConfig.ID })
            {
                ModUtil.AddBuildingToPlanScreen((HashedString)"Base", id);
                var tech = Db.Get().Techs.Get("AdvancedFiltration");
                if (tech != null && !tech.unlockedItemIDs.Contains(id)) tech.unlockedItemIDs.Add(id);
            }
        }
    }
    public class MembraneTileConfig : IBuildingConfig
    {
        public const string ID = "BaiyeSelectiveMembraneTile";
        public override BuildingDef CreateBuildingDef() => Create(ID, false);
        internal static BuildingDef Create(string id, bool door)
        {
            var def = BuildingTemplates.CreateBuildingDef(id, 1, door ? 2 : 1, door ? MembraneVisual.DoorAnim : MembraneVisual.TileAnim, 100, 15f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER3, MATERIALS.REFINED_METALS, 1600f, BuildLocationRule.Tile,
                noise: NOISE_POLLUTION.NONE, decor: BUILDINGS.DECOR.BONUS.TIER0);
            BuildingTemplates.CreateFoundationTileDef(def);
            def.Overheatable = false; def.Floodable = false; def.Entombable = false;
            def.UseStructureTemperature = false; def.TileLayer = ObjectLayer.FoundationTile;
            def.PermittedRotations = PermittedRotations.R90;
            def.SceneLayer = Grid.SceneLayer.TileMain; def.ForegroundLayer = Grid.SceneLayer.InteriorWall;
            def.AudioCategory = "Metal"; def.RequiresPowerInput = false; // Custom native circuit consumers, one per occupied cell.
            // Our KAnim handles art adjacency. Keep native solid ownership but
            // never enlist the vanilla solid-tile atlas or its connector group.
            def.isKAnimTile = false;
            if (!door) def.DragBuild = true;
            if (door) def.LogicInputPorts = new System.Collections.Generic.List<LogicPorts.Port> {
                LogicPorts.Port.InputPort(MembraneDoor.LockPort, new CellOffset(0, 0), "允许通行", "绿：允许通行（仍需人物权限）", "红：拒绝新通行") };
            return def;
        }
        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag) => Configure(go, prefab_tag);
        internal static void Configure(GameObject go, Tag tag)
        {
            GeneratedBuildings.MakeBuildingAlwaysOperational(go);
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation), tag);
            var occupier = go.AddOrGet<SimCellOccupier>();
            occupier.doReplaceElement = true; occupier.setGasImpermeable = true; occupier.setLiquidImpermeable = true; occupier.notifyOnMelt = true;
            go.AddOrGet<TileTemperature>();
        }
        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<MembraneBuilding>();
            go.AddOrGet<MembraneVisual>();
            go.AddOrGet<MembraneFlowPreview>();
            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = new Tag("BaiyeSelectiveMembrane");
            go.GetComponent<KPrefabID>().AddTag(GameTags.FloorTiles);
            go.GetComponent<KBatchedAnimController>().initialAnim = "idle";
            GeneratedBuildings.RemoveLoopingSounds(go);
            Object.DestroyImmediate(go.GetComponent<BuildingEnabledButton>());
        }
        public override void DoPostConfigurePreview(BuildingDef def, GameObject go) { base.DoPostConfigurePreview(def, go); go.AddOrGet<MembraneFlowPreview>(); }
        public override void DoPostConfigureUnderConstruction(GameObject go) { base.DoPostConfigureUnderConstruction(go); go.AddOrGet<MembraneFlowPreview>(); }
    }
    public sealed class MembraneDoorConfig : IBuildingConfig
    {
        public const string ID = "BaiyeSelectiveMembraneDoor";
        public override BuildingDef CreateBuildingDef() => MembraneTileConfig.Create(ID, true);
        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag) => MembraneTileConfig.Configure(go, prefab_tag);
        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<MembraneBuilding>(); go.AddOrGet<MembraneDoor>();
            go.AddOrGet<MembraneVisual>();
            go.AddOrGet<MembraneFlowPreview>();
            go.AddOrGet<AccessControl>().controlEnabled = true;
            go.AddOrGet<KBoxCollider2D>(); go.AddOrGet<ZoneTile>();
            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = new Tag("BaiyeSelectiveMembrane");
            Prioritizable.AddRef(go);
            go.GetComponent<KBatchedAnimController>().initialAnim = "closed";
            GeneratedBuildings.RemoveLoopingSounds(go);
            Object.DestroyImmediate(go.GetComponent<BuildingEnabledButton>());
        }
        public override void DoPostConfigurePreview(BuildingDef def, GameObject go) { base.DoPostConfigurePreview(def, go); go.AddOrGet<MembraneFlowPreview>(); }
        public override void DoPostConfigureUnderConstruction(GameObject go) { base.DoPostConfigureUnderConstruction(go); go.AddOrGet<MembraneFlowPreview>(); }
    }
}
