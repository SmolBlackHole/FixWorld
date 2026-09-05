// SPDX-License-Identifier: MPL-2.0
using System;

namespace FixWorld.Profiling
{
    public enum ProfileSource { FixWorld, RimWorld }

    // Resolve a slot once. Original and replacement share the operation name,
    // but never the source identity or the recorded result.
    public readonly struct ProfileKey : IEquatable<ProfileKey>
    {
        public ProfileKey(string owner, string operation, ProfileSource source = ProfileSource.FixWorld)
        {
            if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner is required.", nameof(owner));
            if (string.IsNullOrWhiteSpace(operation)) throw new ArgumentException("Operation is required.", nameof(operation));
            if (source != ProfileSource.FixWorld && source != ProfileSource.RimWorld)
                throw new ArgumentOutOfRangeException(nameof(source));
            Owner = owner; Operation = operation; Source = source;
        }
        public string Owner { get; }
        public string Operation { get; }
        public ProfileSource Source { get; }
        public bool Equals(ProfileKey other) => Owner == other.Owner && Operation == other.Operation && Source == other.Source;
        public override bool Equals(object obj) => obj is ProfileKey other && Equals(other);
        public override int GetHashCode() => unchecked(((Owner?.GetHashCode() ?? 0) * 397 ^ (Operation?.GetHashCode() ?? 0)) * 397 ^ (int)Source);
        public override string ToString() => Owner + "/" + Operation + "/" + Source;
    }
}
