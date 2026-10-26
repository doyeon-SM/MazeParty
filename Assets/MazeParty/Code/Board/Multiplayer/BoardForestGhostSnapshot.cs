using System;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public struct BoardForestGhostSnapshot : INetworkSerializable, IEquatable<BoardForestGhostSnapshot>
    {
        public bool Active, Strong, Applied, Paused;
        public int Revision, Slot, Amount, ActualAmount;
        public byte Resource;
        public Vector3 Position;
        public Quaternion Facing;
        public double StartedAt, PausedAt;
        public double Elapsed(double now) => Math.Max(0d, (Paused ? PausedAt : now) - StartedAt);
        public bool HasReached(double now, double offset) => (Paused ? PausedAt : now) >= StartedAt + offset;
        public void Pause(double now) { if (!Active || Paused) return; Paused = true; PausedAt = now; }
        public void Resume(double now)
        { if (!Paused) return; StartedAt += Math.Max(0d, now - PausedAt); Paused = false; PausedAt = 0d; }
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Active); s.SerializeValue(ref Strong); s.SerializeValue(ref Applied);
            s.SerializeValue(ref Paused); s.SerializeValue(ref Revision); s.SerializeValue(ref Slot);
            s.SerializeValue(ref Amount); s.SerializeValue(ref ActualAmount); s.SerializeValue(ref Resource);
            s.SerializeValue(ref Position); s.SerializeValue(ref Facing);
            s.SerializeValue(ref StartedAt); s.SerializeValue(ref PausedAt);
        }
        public bool Equals(BoardForestGhostSnapshot o) => Active == o.Active && Strong == o.Strong &&
            Applied == o.Applied && Paused == o.Paused && Revision == o.Revision && Slot == o.Slot &&
            Amount == o.Amount && ActualAmount == o.ActualAmount && Resource == o.Resource &&
            Position == o.Position && Facing == o.Facing && StartedAt == o.StartedAt && PausedAt == o.PausedAt;
        public override bool Equals(object obj) => obj is BoardForestGhostSnapshot o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Revision, Slot, StartedAt, Applied);
    }
}
