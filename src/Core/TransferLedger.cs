using System;
using System.Collections.Generic;
using System.Linq;

namespace Baiye.SelectiveMembrane.Core
{
    public enum MediumPhase { Gas, Liquid }
    public sealed class Parcel
    {
        public double Mass;
        public double Temperature;
        public byte Disease = 255;
        public int Germs;
    }
    public sealed class EmitTicket
    {
        public long Id;
        public double Mass;
        public double Temperature;
        public byte Disease;
        public int Germs;
    }
    // Confirmed stock remains owned while an emission is in flight. Native
    // callback handles never enter the persistent snapshot.
    public sealed class TransferLedger
    {
        public string Element;
        public MediumPhase Phase;
        public int Source = -1, Target = -1;
        public long Sequence;
        public List<Parcel> Parcels = new List<Parcel>();
        public long IntakeId { get; private set; }
        public double Reserved { get; private set; }
        public EmitTicket Emission { get; private set; }
        public double Capacity => Phase == MediumPhase.Gas ? .5 : 2;
        public double Mass => Parcels.Sum(p => p.Mass);
        public double Occupied => Mass + Reserved;
        public bool InFlight => IntakeId != 0 || Emission != null;
        public bool Empty => Parcels.Count == 0 && !InFlight;
        public long BeginIntake(string element, MediumPhase phase, double mass, int source, int target, byte disease)
        {
            if (IntakeId != 0 || !Finite(mass) || mass <= 0 || string.IsNullOrEmpty(element)) return 0;
            if (!Empty && (Element != element || Phase != phase)) return 0;
            double capacity = phase == MediumPhase.Gas ? .5 : 2;
            if (mass > capacity - Occupied) return 0;
            if (Empty) { Element = element; Phase = phase; Source = source; Target = target; }
            IntakeId = ++Sequence; Reserved = mass;
            return IntakeId;
        }
        public bool AcceptIntake(long id, double mass, double temperature, byte disease, int germs)
        {
            if (id == 0 || id != IntakeId) return false;
            if (!Finite(mass) || mass < 0 || !Finite(temperature) || germs < 0) throw new InvalidOperationException("Invalid native intake result");
            IntakeId = 0; Reserved = 0;
            if (mass > 0) Parcels.Add(new Parcel { Mass = mass, Temperature = temperature, Disease = disease, Germs = germs });
            UnlockIfEmpty(); return true;
        }
        public EmitTicket BeginEmit(double maximum)
        {
            if (Emission != null || Parcels.Count == 0 || !Finite(maximum) || maximum <= 0) return null;
            var p = Parcels[0];
            double amount = Math.Min(p.Mass, maximum);
            int germs = amount == p.Mass ? p.Germs : (int)Math.Floor(p.Germs * (amount / p.Mass));
            return Emission = new EmitTicket { Id = ++Sequence, Mass = amount, Temperature = p.Temperature, Disease = p.Disease, Germs = germs };
        }
        public bool FinishEmit(long id, bool succeeded)
        {
            if (Emission == null || id != Emission.Id) return false;
            if (succeeded)
            {
                var p = Parcels[0];
                p.Mass -= Emission.Mass; p.Germs -= Emission.Germs;
                if (p.Mass == 0) Parcels.RemoveAt(0); // Never discard a physical tail by epsilon.
            }
            Emission = null; UnlockIfEmpty(); return true;
        }
        void UnlockIfEmpty() { if (Empty) Element = null; }
        public void ValidateForSave()
        {
            if (InFlight) throw new InvalidOperationException("Native transactions must settle before saving");
            if (Parcels.Any(p => !Finite(p.Mass) || p.Mass <= 0 || !Finite(p.Temperature) || p.Germs < 0)) throw new InvalidOperationException("Invalid stock");
        }
        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
