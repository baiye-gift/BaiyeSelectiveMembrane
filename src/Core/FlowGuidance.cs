using System;
using System.Collections.Generic;

namespace Baiye.SelectiveMembrane.Core
{
    // Same geometry for simulation, construction guidance and selection overlay.
    public static class FlowGuidance
    {
        public static System.Tuple<int, int> Endpoints(int cell, bool vertical, int width, int count)
        {
            if (width <= 0 || cell < 0 || cell >= count) return Tuple.Create(-1, -1);
            int a = vertical ? cell - width : cell % width == 0 ? -1 : cell - 1;
            int b = vertical ? cell + width : cell % width == width - 1 ? -1 : cell + 1;
            return Tuple.Create(a >= 0 && a < count ? a : -1, b >= 0 && b < count ? b : -1);
        }
        public static string Axis(bool vertical) => vertical ? "上 ↕ 下" : "左 ↔ 右";
        public static string Obstruction(bool vertical, bool first, bool second)
        {
            string sides = vertical ? (first && second ? "上下" : first ? "下侧" : "上侧") : (first && second ? "左右" : first ? "左侧" : "右侧");
            return !first && !second ? "两侧无实体阻挡（不代表已有介质流动）" : sides + "被挡；" + (vertical ? "检查上下出口或旋转为左右" : "检查左右出口或旋转为上下");
        }
        public static string Waiting(TransferLedger stock, BuildingState state, CellSample a, CellSample b, int world, Func<int, CellSample> read)
        {
            if (!a.Valid || !b.Valid || a.World != world || b.World != world) return "输运端点超出当前世界";
            if (!stock.Empty)
            {
                var exit = read(stock.Target);
                if (!exit.Valid || exit.World != world || exit.Solid) return "缓存出口被挡；等待出口恢复";
                if (!exit.Accepts(stock.Element)) return "缓存出口是其他介质；等待兼容出口";
                if (TransferPolicy.Headroom(state.Applied, exit, stock.Phase) <= 0) return "出口达到目标上限；可在更多设置调整";
                return "缓存等待投递";
            }
            if (a.Solid || b.Solid) return "输运轴被实体阻挡；检查轴向和两侧格子";
            var cfg = state.Applied;
            var from = cfg.Active ? (cfg.Direction == 1 ? a : b) : (a.Mass > b.Mass ? a : b);
            var to = from.Cell == a.Cell ? b : a;
            if (from.Vacuum || from.Mass <= 0) return "入口没有可输运介质";
            if (!cfg.Allowed.Contains(from.Element)) return "入口介质未被允许；请选择介质";
            if (!cfg.Active && !to.Accepts(from.Element)) return "对侧是其他介质；被动模式等待兼容出口";
            if (!cfg.Active && from.Phase == MediumPhase.Liquid && to.Y > from.Y) return "被动模式不能向上输液；切换主动模式";
            if (to.Accepts(from.Element) && TransferPolicy.Headroom(cfg, to, from.Phase) <= 0) return "出口达到目标上限；可在更多设置调整";
            return !cfg.Active ? "两侧已平衡；等待质量差" : "等待可用输运条件";
        }
    }
    public static class SettingsCommands
    {
        public static bool Quick(BuildingState state, IEnumerable<TransferLedger> units, Action<MembraneSettings> change)
        {
            if (state.Pending != null || state.Demolishing) return false;
            var next = state.Applied.Clone(); change(next);
            return state.Request(next, units);
        }
    }
}
