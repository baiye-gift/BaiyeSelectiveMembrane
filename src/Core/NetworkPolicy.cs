using System;
using System.Collections.Generic;
using System.Linq;

namespace Baiye.SelectiveMembrane.Core
{
    public sealed class NetworkNode
    {
        public int Id, World, Priority = 5;
        public int[] Cells;
    }
    public sealed class InputCandidate
    {
        public int Node, World, Cell, Priority;
        public ushort Circuit;
    }
    public static class NetworkPolicy
    {
        public static List<int[]> Components(IEnumerable<NetworkNode> nodes, int width)
        {
            var all = nodes.ToDictionary(n => n.Id);
            var occupancy = new Dictionary<int, NetworkNode>();
            foreach (var n in all.Values) foreach (int cell in n.Cells) occupancy[cell] = n;
            var seen = new HashSet<int>(); var result = new List<int[]>();
            foreach (var node in all.Values.OrderBy(n => n.Id))
            {
                if (!seen.Add(node.Id)) continue;
                var queue = new Queue<NetworkNode>(); var members = new List<int>(); queue.Enqueue(node);
                while (queue.Count != 0)
                {
                    var n = queue.Dequeue(); members.Add(n.Id);
                    foreach (int cell in n.Cells) foreach (int next in Neighbors(cell, width))
                        if (occupancy.TryGetValue(next, out var neighbor) && neighbor.World == n.World && seen.Add(neighbor.Id)) queue.Enqueue(neighbor);
                }
                result.Add(members.ToArray());
            }
            return result;
        }
        public static IEnumerable<int> Neighbors(int cell, int width)
        {
            if (cell % width != 0) yield return cell - 1;
            if (cell % width != width - 1) yield return cell + 1;
            if (cell >= width) yield return cell - width;
            yield return cell + width;
        }
        public static InputCandidate[] Deduplicate(IEnumerable<InputCandidate> inputs) => inputs.Where(i => i.Circuit != ushort.MaxValue).OrderByDescending(i => i.Priority).ThenBy(i => i.World).ThenBy(i => i.Cell).GroupBy(i => i.Circuit).Select(g => g.First()).ToArray();
        public static double Fraction(double paidJoules, double demandWatts, double dt) => demandWatts <= 0 || dt <= 0 ? 0 : Math.Max(0, Math.Min(1, paidJoules / (demandWatts * dt)));
    }
    public sealed class SourceElection
    {
        public int CurrentCell = -1;
        double shortage, stable, cooldown;
        int returnCell = -1;
        bool trial;
        public int Choose(InputCandidate[] ranked, double lastFraction, double dt, bool working)
        {
            cooldown = Math.Max(0, cooldown - dt);
            if (ranked.Length == 0) { CurrentCell = -1; returnCell = -1; trial = false; return -1; }
            if (!ranked.Any(i => i.Cell == CurrentCell)) { CurrentCell = ranked[0].Cell; shortage = stable = 0; trial = false; }
            if (!working) return CurrentCell;
            shortage = lastFraction < .999 ? shortage + dt : 0;
            if (trial)
            {
                if (lastFraction < .999) { CurrentCell = returnCell; trial = false; cooldown = 5; stable = shortage = 0; }
                else if ((stable += dt) >= 5) { trial = false; returnCell = -1; stable = 0; }
                return CurrentCell;
            }
            if (shortage >= 2 && ranked.Length > 1)
            {
                int index = Array.FindIndex(ranked, i => i.Cell == CurrentCell);
                CurrentCell = ranked[(index + 1) % ranked.Length].Cell; shortage = stable = 0; cooldown = 5;
            }
            else if (CurrentCell != ranked[0].Cell && cooldown == 0)
            {
                returnCell = CurrentCell; CurrentCell = ranked[0].Cell; trial = true; stable = shortage = 0;
            }
            return CurrentCell;
        }
    }
}
