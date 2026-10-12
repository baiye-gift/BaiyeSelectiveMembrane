using System.Collections.Generic;

namespace Baiye.SelectiveMembrane.Core
{
    // Serialize passive equalizations sharing either endpoint. The claim spans
    // consume, held stock, emission and failed-output retries.
    public sealed class PassiveClaims
    {
        readonly Dictionary<int, TransferLedger> cells = new Dictionary<int, TransferLedger>();
        public bool Claim(TransferLedger owner, int source, int target)
        {
            if ((cells.TryGetValue(source, out var s) && s != owner) || (cells.TryGetValue(target, out var t) && t != owner)) return false;
            cells[source] = cells[target] = owner; return true;
        }
        public void Settle(TransferLedger owner)
        {
            if (!owner.Empty) return;
            var remove = new List<int>(); foreach (var pair in cells) if (pair.Value == owner) remove.Add(pair.Key);
            foreach (int cell in remove) cells.Remove(cell);
        }
        public void Clear() => cells.Clear();
    }
}
