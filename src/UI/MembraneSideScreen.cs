using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Baiye.SelectiveMembrane.Core;

namespace Baiye.SelectiveMembrane
{
    // DetailsScreen owns the panel. Editing sessions belong to building
    // instances; live refresh never rewrites text fields or the draft.
    public sealed class MembraneSideScreen : SideScreenContent
    {
        public LayoutElement ButtonTemplate;
        public LocText LabelTemplate;
        public KInputTextField InputTemplate;
        MembraneBuilding target;
        EditSession session;
        readonly Dictionary<int, EditSession> sessions = new Dictionary<int, EditSession>();
        readonly Dictionary<KButton, LocText> captions = new Dictionary<KButton, LocText>();
        // Keep the complete clone root: active-only hierarchy queries cannot
        // recover controls while DetailsScreen or the control itself is hidden.
        readonly Dictionary<KButton, GameObject> buttonRoots = new Dictionary<KButton, GameObject>();
        LocText status, current, editStatus, internalInfo, networkInfo, bulkInfo, copyInfo;
        KButton pause, mode, direction, elements, apply, undo, cancelPending, cancelDemolition, doorLock, priorityApply, bulkApply, more;
        KInputTextField gas, liquid, priority;
        KButton gasUnlimited, liquidUnlimited;
        GameObject doorRoot, priorityRoot, advancedActions;
        readonly List<KButton> sectionButtons = new List<KButton>();
        bool showMore;
        readonly List<GameObject> detailBodies = new List<GameObject>();
        GameObject detailViewport;
        LayoutElement detailSize, panelSize;
        bool built, gasValid = true, liquidValid = true;
        int openSection = -1;
        float nextRefresh;
        int[] bulkSnapshot;
        MembraneSettings bulkSettings;
        string bulkReceipt = "先预览当前网络，再应用。";
        GameObject popup;
        MembraneBuilding popupOwner;
        int popupRevision;
        HashSet<string> popupSelection;
        KInputTextField search;
        LocText selectionCount;
        GameObject elementList;
        bool liquidTab;
        string lastSearch = "";
        List<Element> visibleElements = new List<Element>();
        public override bool IsValidForTarget(GameObject go) => go != null && go.GetComponent<MembraneBuilding>()?.Ready == true;
        public override int GetSideScreenSortOrder() => 90;
        public override string GetTitle() => "分子筛设置";
        public override void SetTarget(GameObject go)
        {
            FinishEditing(); ClosePopup(); target = go?.GetComponent<MembraneBuilding>();
            if (target == null || !target.Ready) { target = null; session = null; return; }
            if (!sessions.TryGetValue(target.GetInstanceID(), out session)) sessions[target.GetInstanceID()] = session = new EditSession(target.State);
            Build(); LoadFields(); bulkSnapshot = null; bulkSettings = null; bulkReceipt = "先预览当前网络，再应用。"; RefreshView();
        }
        public override void ClearTarget() { FinishEditing(); ClosePopup(); target = null; session = null; }
        void FinishEditing() { gas?.DeactivateInputField(); liquid?.DeactivateInputField(); priority?.DeactivateInputField(); }
        protected override void OnShow(bool show) { if (!show) ClosePopup(); base.OnShow(show); }
        protected override void OnCmpDisable()
        {
            // Native DetailsScreen/tab changes use SetActive, not OnShow.
            FinishEditing(); ClosePopup(); base.OnCmpDisable();
        }
        protected override void OnCleanUp()
        {
            ClosePopup(); target = null; session = null;
            sessions.Clear(); captions.Clear(); buttonRoots.Clear();
            base.OnCleanUp();
        }
        void Update()
        {
            if (target == null || !target.Ready) { ClosePopup(); return; }
            if (!isActiveAndEnabled) return;
            if (popup != null && popupOwner == target && Input.compositionString.Length == 0 && search.text != lastSearch) RebuildElementList();
            if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .5f; RefreshView(); }
        }
        void Build()
        {
            if (built) return;
            panelSize = gameObject.AddOrGet<LayoutElement>(); panelSize.minWidth = 280; panelSize.preferredWidth = 280; panelSize.flexibleHeight = 0;
            var layout = gameObject.AddOrGet<VerticalLayoutGroup>(); ConfigureLayout(layout, 4); layout.padding = new RectOffset(8, 8, 6, 6);
            status = Label(gameObject, 48); current = Label(gameObject, 24);
            elements = Button(gameObject, OpenElements);
            var controls = Row(gameObject);
            mode = Button(controls, () => QuickChange(cfg => cfg.Active = !cfg.Active));
            pause = Button(controls, () => target.Pause());
            direction = Button(gameObject, () => QuickChange(cfg => cfg.Direction *= -1));
            cancelPending = Button(gameObject, () => { target.State.CancelPending(); session.Reset(target.State); LoadFields(); });
            cancelDemolition = Button(gameObject, () => target.CancelDemolition());
            doorRoot = Group(gameObject, "Door"); doorLock = Button(doorRoot, () => target.GetComponent<MembraneDoor>()?.ToggleLock());
            more = Button(gameObject, () => { showMore = !showMore; if (openSection < 0) openSection = 1; RefreshFolds(); });
            detailViewport = new GameObject("DetailsViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            detailViewport.layer = gameObject.layer; detailViewport.transform.SetParent(transform, false);
            detailViewport.GetComponent<Image>().color = Color.clear;
            detailSize = detailViewport.GetComponent<LayoutElement>();
            var content = Group(detailViewport, "Details"); var cr = content.GetComponent<RectTransform>();
            cr.anchorMin = new Vector2(0, 1); cr.anchorMax = Vector2.one; cr.pivot = new Vector2(.5f, 1); cr.sizeDelta = Vector2.zero;
            content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = detailViewport.GetComponent<ScrollRect>(); scroll.viewport = detailViewport.GetComponent<RectTransform>(); scroll.content = cr; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            string[] sections = { "缓存", "上限", "供电", "批量" };
            var tabs = Row(content);
            for (int i = 0; i < sections.Length; i++)
            {
                int index = i;
                var button = Button(tabs, () => { openSection = index; RefreshFolds(); });
                sectionButtons.Add(button); Caption(button, sections[i], true);
                var body = Group(content, sections[i]); detailBodies.Add(body); body.SetActive(false);
            }
            internalInfo = Label(detailBodies[0], 70);
            gasUnlimited = Button(detailBodies[1], () => { session.Draft.GasLimit = session.Draft.GasLimit.HasValue ? (double?)null : 0; ParseLimit(session.GasText, false); });
            gas = InputField(detailBodies[1], text => ParseLimit(text, false));
            liquidUnlimited = Button(detailBodies[1], () => { session.Draft.LiquidLimit = session.Draft.LiquidLimit.HasValue ? (double?)null : 0; ParseLimit(session.LiquidText, true); });
            liquid = InputField(detailBodies[1], text => ParseLimit(text, true));
            Label(detailBodies[1], 36).SetText("目标上限：kg/格；0 为明确禁止新增介质。\n输入须为有限非负数。库存会按旧配置排空。");
            editStatus = Label(detailBodies[1], 40);
            advancedActions = Row(detailBodies[1]);
            apply = Button(advancedActions, CommitDraft); undo = Button(advancedActions, () => { session.Reset(target.State); LoadFields(); });
            networkInfo = Label(detailBodies[2], 90);
            priorityRoot = Group(detailBodies[2], "EntryPriority");
            Label(priorityRoot, 24).SetText("此建筑入口优先级（1—9，9 最高）");
            priority = InputField(priorityRoot, text => { session.PriorityText = text; RefreshView(); });
            priorityApply = Button(priorityRoot, () => { if (int.TryParse(priority.text, out int value) && value >= 1 && value <= 9) target.State.InputPriority = value; });
            copyInfo = Label(detailBodies[2], 34);
            var preview = Button(detailBodies[3], PreviewNetwork); Caption(preview, "预览当前网络目标", true);
            bulkInfo = Label(detailBodies[3], 75); bulkApply = Button(detailBodies[3], ApplyNetwork);
            RefreshFolds();
            // Input activation can emit callbacks during construction.
            built = true;
        }
        void ParseLimit(string text, bool isLiquid)
        {
            if (target == null || session == null) return;
            if (isLiquid) session.LiquidText = text; else session.GasText = text;
            bool valid = MembraneSettings.TryLimit(text, CultureInfo.CurrentCulture, out double value);
            if (isLiquid) { liquidValid = valid || !session.Draft.LiquidLimit.HasValue; if (valid && session.Draft.LiquidLimit.HasValue) session.Draft.LiquidLimit = value; }
            else { gasValid = valid || !session.Draft.GasLimit.HasValue; if (valid && session.Draft.GasLimit.HasValue) session.Draft.GasLimit = value; }
            RefreshView();
        }
        void LoadFields()
        {
            gasValid = !session.Draft.GasLimit.HasValue || MembraneSettings.TryLimit(session.GasText, CultureInfo.CurrentCulture, out _);
            liquidValid = !session.Draft.LiquidLimit.HasValue || MembraneSettings.TryLimit(session.LiquidText, CultureInfo.CurrentCulture, out _);
            gas.SetTextWithoutNotify(session.GasText); liquid.SetTextWithoutNotify(session.LiquidText);
            priority.SetTextWithoutNotify(session.PriorityText);
        }
        bool CanEdit => built && target != null && target.Ready && session != null && target.State.Pending == null && !target.State.Demolishing;
        bool ValidDraft => (!session.Draft.GasLimit.HasValue || MembraneSettings.TryLimit(gas.text, CultureInfo.CurrentCulture, out _)) && (!session.Draft.LiquidLimit.HasValue || MembraneSettings.TryLimit(liquid.text, CultureInfo.CurrentCulture, out _)) && session.Draft.Valid && Input.compositionString.Length == 0;
        void CommitDraft()
        {
            if (target == null || session == null) return;
            if (session.ObserveAdvanced(target.State)) LoadFields();
            if (!CanEdit || !ValidDraft || session.Stale) return;
            CaptureLimitFields();
            var cfg = target.State.Applied.Clone();
            cfg.GasLimit = session.Draft.GasLimit; cfg.LiquidLimit = session.Draft.LiquidLimit;
            if (target.Apply(cfg)) { session.Reset(target.State); LoadFields(); }
        }
        void QuickChange(System.Action<MembraneSettings> change)
        {
            if (!CanEdit || Input.compositionString.Length != 0) return;
            if (session.ObserveAdvanced(target.State)) LoadFields();
            if (SettingsCommands.Quick(target.State, target.Units, change)) session.ObserveAdvanced(target.State);
        }
        void CaptureLimitFields()
        {
            if (session.Draft.GasLimit.HasValue) { MembraneSettings.TryLimit(gas.text, CultureInfo.CurrentCulture, out double value); session.Draft.GasLimit = value; }
            if (session.Draft.LiquidLimit.HasValue) { MembraneSettings.TryLimit(liquid.text, CultureInfo.CurrentCulture, out double value); session.Draft.LiquidLimit = value; }
        }
        void PreviewNetwork()
        {
            if (target == null || session == null) return;
            if (session.ObserveAdvanced(target.State)) LoadFields();
            if (!CanEdit || !ValidDraft || session.Stale || target.Network == null) return;
            CaptureLimitFields();
            bulkSnapshot = target.Network.Buildings.Where(b => b.Ready).Select(b => b.GetInstanceID()).ToArray();
            bulkSettings = session.Draft.Clone();
            bulkReceipt = $"固定目标 {bulkSnapshot.Length} 座；{target.Network.UnitCount} 个处理单元。\n模式：{(bulkSettings.Active ? "主动" : "被动")}；白名单 {bulkSettings.Allowed.Count} 项。\n不同轴向保留各自原方向。";
        }
        void ApplyNetwork()
        {
            if (bulkSnapshot == null || bulkSettings == null) return;
            int applied = 0, waiting = 0, skipped = 0;
            bool vertical = target.Vertical;
            foreach (int id in bulkSnapshot)
            {
                if (!MembraneNetworks.Buildings.TryGetValue(id, out var building) || !building.Ready || building.State.Pending != null || building.State.Demolishing) { skipped++; continue; }
                var cfg = bulkSettings.Clone(); if (building.Vertical != vertical) cfg.Direction = building.State.Applied.Direction;
                if (!building.Apply(cfg)) { skipped++; continue; }
                if (building.State.Pending != null) waiting++; else applied++;
            }
            bulkReceipt = $"已应用 {applied}；等待排空 {waiting}；跳过 {skipped}。\n预览后新增建筑未纳入本次应用。";
            bulkSnapshot = null; bulkSettings = null; if (session.ObserveAdvanced(target.State)) LoadFields();
        }
        string DirectionName(MembraneSettings cfg) => target.Vertical ? (cfg.Direction == 1 ? "向上" : "向下") : (cfg.Direction == 1 ? "向右" : "向左");
        void RefreshView()
        {
            if (!built || target == null || !target.Ready || session == null) return;
            if (session.ObserveAdvanced(target.State)) LoadFields();
            status.SetText(target.Status());
            var live = target.State.Pending ?? target.State.Applied;
            current.SetText(FlowGuidance.Axis(target.Vertical) + " · " + (target.State.Applied.Active ? "主动（需供电）" : "被动（无需电）"));
            Caption(pause, target.State.Paused ? "恢复" : "暂停", true);
            Caption(mode, "模式：" + (live.Active ? "主动" : "被动"), CanEdit);
            buttonRoots[direction].SetActive(live.Active);
            Caption(direction, "方向：" + DirectionName(live) + " [切换]", CanEdit);
            string names = string.Join("、", live.Allowed.Take(2).Select(ElementName));
            Caption(elements, "允许：" + (names.Length == 0 ? "选择介质" : names) + (live.Allowed.Count > 2 ? $" +{live.Allowed.Count - 2}" : "") + " [更改]", CanEdit);
            bool dirty = session.AdvancedDirty;
            Caption(apply, "保存上限", CanEdit && ValidDraft && !session.Stale && dirty);
            Caption(undo, session.Stale ? "重新载入上限" : "撤销上限修改", session.Stale || CanEdit && (dirty || !ValidDraft));
            advancedActions.SetActive(dirty || session.Stale || !ValidDraft);
            editStatus.SetText(session.Stale ? "上限被外部修改；重新载入后编辑。" : !ValidDraft ? "数值无效；请输入有限非负数。" : dirty ? "上限尚未保存；其他操作不会提交这些数值。" : "常用设置直接生效；这里只保存上限。");
            buttonRoots[cancelPending].SetActive(target.State.Pending != null);
            Caption(cancelPending, "取消待应用配置", target.State.Pending != null);
            buttonRoots[cancelDemolition].SetActive(target.State.Demolishing);
            Caption(cancelDemolition, "取消拆除并保留库存", target.State.Demolishing);
            var door = target.GetComponent<MembraneDoor>(); doorRoot.SetActive(door != null);
            if (door != null) Caption(doorLock, (door.ManualLock ? "手动锁定 [解除]" : "自动通行 [锁定]") + " · " + (!door.Wired ? "未接自动化" : door.AutomationAllows ? "绿" : "红"), true);
            internalInfo.SetText(string.Join("\n", target.Units.Select((u, i) => (target.Units.Length == 1 ? "单元" : target.Vertical ? (i == 0 ? "左单元" : "右单元") : (i == 0 ? "下单元" : "上单元")) + "：" + (u.Element == null ? "空" : ElementName(u.Element) + $" {u.Mass:F4}/{u.Capacity:G} kg") + (u.InFlight ? " · 转移中" : ""))));
            Caption(gasUnlimited, "气体目标上限：" + (session.Draft.GasLimit.HasValue ? "有限 [改为无限制]" : "无限制 [启用上限]"), CanEdit);
            Caption(liquidUnlimited, "液体目标上限：" + (session.Draft.LiquidLimit.HasValue ? "有限 [改为无限制]" : "无限制 [启用上限]"), CanEdit);
            gas.interactable = CanEdit && popup == null && session.Draft.GasLimit.HasValue; liquid.interactable = CanEdit && popup == null && session.Draft.LiquidLimit.HasValue;
            priority.interactable = popup == null;
            var network = target.Network;
            networkInfo.SetText(network == null ? "网络尚未就绪" : $"建筑 {network.Buildings.Length}；单元 {network.UnitCount}\n需求 {network.Demand:F0} W；实际支付 {network.Paid / .2:F1} W\n工作比例 {network.Fraction:P0}；近 5 秒交付 {target.RecentRate:F4} kg/s\n主入口格 {network.Election.CurrentCell}；独立电路 {network.Candidates.Length}");
            priorityRoot.SetActive(target.Inputs.Any(i => Game.Instance.circuitManager.GetCircuitID(i.PowerCell) != ushort.MaxValue));
            Caption(priorityApply, "应用入口优先级", int.TryParse(priority.text, out int p) && p >= 1 && p <= 9 && p != target.State.InputPriority);
            copyInfo.SetText(target.LastCopyResult);
            bulkInfo.SetText(bulkReceipt); Caption(bulkApply, "应用到预览中的建筑", bulkSnapshot != null);
            Caption(more, showMore ? "收起更多设置" : "更多设置", true);
            Resize();
        }
        void RefreshFolds()
        {
            for (int i = 0; i < detailBodies.Count; i++) detailBodies[i].SetActive(openSection == i);
            detailViewport.SetActive(showMore);
            string[] names = { "缓存", "上限", "供电", "批量" };
            for (int i = 0; i < sectionButtons.Count; i++) Caption(sectionButtons[i], (openSection == i ? "· " : "") + names[i], true);
            Resize();
        }
        void Resize()
        {
            if (panelSize == null) return;
            var scaler = GameScreenManager.Instance.ssOverlayCanvas.GetComponent<KCanvasScaler>();
            float scale = scaler == null ? 1 : Mathf.Max(.01f, scaler.GetCanvasScale());
            float maxDetails = Mathf.Clamp(Screen.height / scale - 410, 140, 260);
            detailSize.minHeight = detailSize.preferredHeight = maxDetails;
            panelSize.preferredHeight = -1; panelSize.minHeight = 0;
            LayoutRebuilder.MarkLayoutForRebuild(GetComponent<RectTransform>());
        }
        void OpenElements()
        {
            if (!CanEdit || popup != null) return;
            popupOwner = target; popupRevision = target.State.Revision; popupSelection = new HashSet<string>(target.State.Applied.Allowed);
            popup = new GameObject("MembraneElementChooser", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            popup.layer = gameObject.layer; popup.transform.SetParent(GameScreenManager.Instance.ssOverlayCanvas.transform, false);
            var r = popup.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(.5f, .5f); r.pivot = new Vector2(.5f, .5f);
            var scaler = GameScreenManager.Instance.ssOverlayCanvas.GetComponent<KCanvasScaler>(); float scale = scaler == null ? 1 : Mathf.Max(.01f, scaler.GetCanvasScale());
            r.sizeDelta = new Vector2(Mathf.Min(480, Screen.width / scale - 40), Mathf.Min(500, Screen.height / scale - 60));
            popup.GetComponent<Image>().color = new Color(.16f, .17f, .22f, 1);
            var layout = popup.GetComponent<VerticalLayoutGroup>(); ConfigureLayout(layout, 6); layout.padding = new RectOffset(12, 12, 12, 12);
            Label(popup, 25).SetText("选择允许通过的介质");
            var tabs = Row(popup);
            var gases = Button(tabs, () => { liquidTab = false; RebuildElementList(); }); Caption(gases, "气体", true);
            var liquids = Button(tabs, () => { liquidTab = true; RebuildElementList(); }); Caption(liquids, "液体", true);
            search = InputField(popup, _ => { }); search.SetTextWithoutNotify("");
            var clearSearch = Button(tabs, () => { search.SetTextWithoutNotify(""); RebuildElementList(); search.Select(); search.ActivateInputField(); }); Caption(clearSearch, "清搜索", true);
            var listViewport = new GameObject("ElementListViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect), typeof(LayoutElement));
            listViewport.layer = gameObject.layer; listViewport.transform.SetParent(popup.transform, false); listViewport.GetComponent<Image>().color = Color.clear;
            var size = listViewport.GetComponent<LayoutElement>(); size.minHeight = 90; size.flexibleHeight = 1;
            elementList = Group(listViewport, "Elements"); var cr = elementList.GetComponent<RectTransform>(); cr.anchorMin = new Vector2(0, 1); cr.anchorMax = Vector2.one; cr.pivot = new Vector2(.5f, 1); cr.sizeDelta = Vector2.zero;
            elementList.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = listViewport.GetComponent<ScrollRect>(); scroll.viewport = listViewport.GetComponent<RectTransform>(); scroll.content = cr; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
            selectionCount = Label(popup, 28);
            var selectionActions = Row(popup);
            var select = Button(selectionActions, () => { foreach (var e in visibleElements) popupSelection.Add(e.id.ToString()); RebuildElementList(); }); Caption(select, "选择当前列表", true);
            var clear = Button(selectionActions, () => { foreach (var e in visibleElements) popupSelection.Remove(e.id.ToString()); RebuildElementList(); }); Caption(clear, "清除当前列表", true);
            var footer = Row(popup);
            var use = Button(footer, ConfirmElements); Caption(use, "确认并启用", true);
            var cancel = Button(footer, ClosePopup); Caption(cancel, "取消选择", true);
            RebuildElementList(); search.Select(); search.ActivateInputField();
        }
        void ConfirmElements()
        {
            if (popupOwner != target || !CanEdit || Input.compositionString.Length != 0) return;
            if (popupRevision != target.State.Revision) { selectionCount.SetText("配置已被其他操作修改；取消后重新选择介质。"); return; }
            if (session.ObserveAdvanced(target.State)) LoadFields();
            if (SettingsCommands.Quick(target.State, target.Units, cfg => cfg.Allowed = new HashSet<string>(popupSelection)))
            { session.ObserveAdvanced(target.State); ClosePopup(); }
        }
        void RebuildElementList()
        {
            if (popup == null || popupOwner != target) return;
            lastSearch = search.text;
            foreach (Transform child in elementList.transform)
            {
                foreach (var button in child.GetComponentsInChildren<KButton>(true)) ForgetButton(button);
                child.gameObject.SetActive(false); Destroy(child.gameObject);
            }
            visibleElements = ElementLoader.elements.Where(e => liquidTab ? e.IsLiquid : e.IsGas).Where(e => e.id != SimHashes.Vacuum)
                .Where(e => popupSelection.Contains(e.id.ToString()) || DiscoveredResources.Instance?.IsDiscovered(e.tag) == true)
                .Where(e => ElementName(e.id.ToString()).IndexOf(lastSearch, StringComparison.CurrentCultureIgnoreCase) >= 0 || e.id.ToString().IndexOf(lastSearch, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(e => ElementName(e.id.ToString())).ToList();
            foreach (var element in visibleElements)
            {
                string id = element.id.ToString();
                var b = Button(elementList, () => { if (popupOwner != target) return; if (!popupSelection.Add(id)) popupSelection.Remove(id); RebuildElementList(); });
                Caption(b, (popupSelection.Contains(id) ? "[已选] " : "[未选] ") + ElementName(id), true);
            }
            if (visibleElements.Count == 0) Label(elementList, 36).SetText("当前列表没有匹配介质");
            int unavailable = popupSelection.Count(id => !ElementLoader.elements.Any(e => e.id.ToString() == id));
            selectionCount.SetText($"已选 {popupSelection.Count}；当前列表 {visibleElements.Count}" + (unavailable > 0 ? $"；不可用记录 {unavailable}（保留）" : ""));
        }
        void ClosePopup()
        {
            if (popup == null) return;
            // Release per-popup bindings before destruction, including rows
            // that have been filtered out during the current frame.
            foreach (var b in captions.Keys.Where(b => b != null && b.transform.IsChildOf(popup.transform)).ToArray()) ForgetButton(b);
            popup.SetActive(false); Destroy(popup); popup = null; popupOwner = null; popupSelection = null;
            if (elements != null && elements.gameObject.activeInHierarchy) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(elements.gameObject);
        }
        void ForgetButton(KButton button) { captions.Remove(button); buttonRoots.Remove(button); }
        static string ElementName(string id)
        {
            var element = ElementLoader.elements.FirstOrDefault(e => e.id.ToString() == id);
            return element == null ? id + "（不可用）" : global::STRINGS.UI.StripLinkFormatting(element.name);
        }
        static void ConfigureLayout(VerticalLayoutGroup layout, int spacing)
        { layout.spacing = spacing; layout.childControlWidth = true; layout.childControlHeight = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false; }
        GameObject Group(GameObject parent, string name)
        { var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup)); go.layer = gameObject.layer; go.transform.SetParent(parent.transform, false); ConfigureLayout(go.GetComponent<VerticalLayoutGroup>(), 4); return go; }
        GameObject Row(GameObject parent)
        {
            var go = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup)); go.layer = gameObject.layer; go.transform.SetParent(parent.transform, false);
            var l = go.GetComponent<HorizontalLayoutGroup>(); l.spacing = 5; l.childControlWidth = true; l.childControlHeight = true; l.childForceExpandWidth = true; l.childForceExpandHeight = false; return go;
        }
        LocText Label(GameObject parent, float height)
        {
            var go = Util.KInstantiateUI(LabelTemplate.gameObject, parent, true); var text = go.GetComponent<LocText>(); text.key = ""; text.raycastTarget = false; text.fontSize = 15; text.textWrappingMode = TMPro.TextWrappingModes.Normal;
            text.color = popup != null && parent.transform.IsChildOf(popup.transform) ? Color.white : new Color(.18f, .20f, .25f, 1);
            var l = go.AddOrGet<LayoutElement>(); l.ignoreLayout = false; l.minHeight = height; l.preferredHeight = -1; l.minWidth = 0; l.flexibleWidth = 1; return text;
        }
        KButton Button(GameObject parent, System.Action action)
        {
            var go = Util.KInstantiateUI(ButtonTemplate.gameObject, parent, true); var button = go.GetComponentInChildren<KButton>(true);
            var l = go.AddOrGet<LayoutElement>(); l.ignoreLayout = false; l.minHeight = 34; l.preferredHeight = 34; l.minWidth = 0; l.flexibleWidth = 1; l.flexibleHeight = 0;
            captions[button] = go.GetComponentInChildren<LocText>(true); captions[button].fontSize = 15;
            buttonRoots[button] = go;
            var popupAtBind = popup; bool popupButton = popup != null && go.transform.IsChildOf(popup.transform);
            button.ClearOnClick(); button.onClick += () => { if (!built || go == null || !go.activeInHierarchy || target == null || !target.Ready || session == null || (popupButton && popup != popupAtBind) || (!popupButton && popup != null)) return; action(); RefreshView(); }; return button;
        }
        void Caption(KButton button, string text, bool enabled) { captions[button].SetText(text); button.isInteractable = enabled && (popup == null || button.transform.IsChildOf(popup.transform)); button.GetComponentInChildren<ToolTip>(true)?.SetSimpleTooltip(text); }
        KInputTextField InputField(GameObject parent, System.Action<string> change)
        {
            var go = Util.KInstantiateUI(InputTemplate.gameObject, parent, false);
            foreach (var oldOwner in go.GetComponents<KInputField>()) DestroyImmediate(oldOwner);
            var field = go.GetComponent<KInputTextField>(); field.onValueChanged.RemoveAllListeners(); field.onEndEdit.RemoveAllListeners(); field.onFocus = null;
            field.contentType = TMPro.TMP_InputField.ContentType.Standard; field.characterValidation = TMPro.TMP_InputField.CharacterValidation.None;
            field.lineType = TMPro.TMP_InputField.LineType.SingleLine; field.characterLimit = 96;
            var l = go.AddOrGet<LayoutElement>(); l.minHeight = l.preferredHeight = 34; l.minWidth = 0; l.flexibleWidth = 1; l.ignoreLayout = false;
            var owner = go.AddComponent<MembraneTextInput>(); AccessTools.Field(typeof(KInputField), "inputField").SetValue(owner, field);
            owner.activateOnSpawn = true;
            field.onValueChanged.AddListener(text => { if (built && target != null && target.Ready && session != null && Input.compositionString.Length == 0) change(text); });
            field.onEndEdit.AddListener(text => { if (built && target != null && target.Ready && session != null && Input.compositionString.Length == 0) change(text); });
            go.SetActive(true); return field;
        }
    }
    public sealed class MembraneTextInput : KInputField { }
    [HarmonyPatch(typeof(DetailsScreen), "OnPrefabInit")]
    internal static class MembraneSideScreenRegistration
    {
        internal static bool Add(List<DetailsScreen.SideScreenRef> screens, MembraneSideScreen screen)
        {
            if (screens.Any(s => s.screenPrefab is MembraneSideScreen)) return false;
            screens.Add(new DetailsScreen.SideScreenRef { name = "BaiyeMembraneSettings", screenPrefab = screen, screenInstance = screen, tab = DetailsScreen.SidescreenTabTypes.Config }); return true;
        }
        static void Postfix(DetailsScreen __instance)
        {
            var screens = (List<DetailsScreen.SideScreenRef>)AccessTools.Field(typeof(DetailsScreen), "sideScreens").GetValue(__instance);
            if (screens.Any(s => s.screenPrefab is MembraneSideScreen)) return;
            var source = screens.Select(s => s.screenPrefab).OfType<ButtonMenuSideScreen>().FirstOrDefault();
            var input = Resources.FindObjectsOfTypeAll<KInputTextField>().FirstOrDefault();
            var config = __instance.GetTabOfType(DetailsScreen.SidescreenTabTypes.Config);
            if (source == null || input == null || config?.bodyInstance == null) { Debug.LogWarning("[BaiyeSelectiveMembrane] Native UI templates unavailable; settings panel was not registered."); return; }
            var go = new GameObject("BaiyeMembraneSettings", typeof(RectTransform), typeof(LayoutElement)); go.SetActive(false); go.layer = source.gameObject.layer; go.transform.SetParent(config.bodyInstance.transform, false);
            var screen = go.AddComponent<MembraneSideScreen>(); screen.ContentContainer = go; screen.ButtonTemplate = source.buttonPrefab; screen.LabelTemplate = source.buttonPrefab.GetComponentInChildren<LocText>(true); screen.InputTemplate = input;
            Add(screens, screen);
        }
    }
}
