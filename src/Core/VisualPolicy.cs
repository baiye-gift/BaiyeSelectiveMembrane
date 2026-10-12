using System;

namespace Baiye.SelectiveMembrane.Core
{
    public enum VisualSignal { Off, Idle, Working, Paused, Waiting, Draining }

    // Art neighbours use W/E/S/N = 1/2/4/8. Never query an invalid cell.
    public static class VisualPolicy
    {
        public static int Neighbor(int cell, int bit, int width, int count)
        {
            if (width <= 0 || cell < 0 || cell >= count) return -1;
            int target;
            switch (bit)
            {
                case 1: if (cell % width == 0) return -1; target = cell - 1; break;
                case 2: if (cell % width == width - 1) return -1; target = cell + 1; break;
                case 4: target = cell - width; break;
                case 8: target = cell + width; break;
                default: return -1;
            }
            return target >= 0 && target < count ? target : -1;
        }
        public static int Mask(int cell, int width, int count, Func<int, bool> connected)
        {
            int mask = 0;
            for (int bit = 1; bit <= 8; bit <<= 1)
            {
                int other = Neighbor(cell, bit, width, count);
                if (other >= 0 && connected(other)) mask |= bit;
            }
            return mask;
        }
        public static int LocalMask(int worldMask, int clockwiseTurns)
        {
            int result = worldMask & 15;
            for (int i = 0; i < ((clockwiseTurns % 4) + 4) % 4; i++)
                result = ((result & 1) << 2) | ((result & 2) << 2) | ((result & 4) >> 1) | ((result & 8) >> 3);
            return result;
        }
        public static VisualSignal Signal(BuildingState state, bool demand, bool paid, double deliveredRate)
        {
            if (state.Demolishing || state.Pending != null) return VisualSignal.Draining;
            if (state.Paused) return VisualSignal.Paused;
            if (state.Applied.Allowed.Count == 0) return VisualSignal.Off;
            if (state.Applied.Active && demand && !paid) return VisualSignal.Waiting;
            return deliveredRate > 0 ? VisualSignal.Working : VisualSignal.Idle;
        }
        public static string DoorClip(bool passage, bool transition) => transition
            ? (passage ? "opening" : "closing") : (passage ? "open" : "closed");
    }
}
