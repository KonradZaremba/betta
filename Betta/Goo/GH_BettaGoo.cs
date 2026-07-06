// Copyright (c) 2026 Konrad Zaremba
// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System;
using Betta.Interfaces;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace Betta.Goo
{
    /// <summary>
    /// Generic IGH_Goo wrapper for opaque Betta domain values. ParamInjector's
    /// existing UnwrapGoo path calls IGH_Goo.ScriptVariable() to get the raw
    /// CLR payload, so a wrapped value comes back out the other side of an
    /// opaque wire as the original T with no extra plumbing.
    ///
    /// Read/Write are implemented but only do work when T implements
    /// <see cref="Betta.Interfaces.IBettaSerializable"/>. By default opaque
    /// values are recomputed by re-solving from wired inputs, not serialized
    /// into the .gh file — implementing IBettaSerializable opts a type into
    /// state persistence at save/reload time.
    /// </summary>
    public class GH_BettaGoo<T> : GH_Goo<T> where T : class
    {
        public GH_BettaGoo() { }
        public GH_BettaGoo(T value) { Value = value; }

        public override bool IsValid => Value != null;

        public override string TypeName => typeof(T).Name;
        public override string TypeDescription => $"Betta opaque value of type {typeof(T).FullName}";

        public override IGH_Goo Duplicate() => new GH_BettaGoo<T>(Value);

        public override string ToString() =>
            Value == null ? $"null {typeof(T).Name}" : Value.ToString();

        // ParamInjector.UnwrapGoo calls this to extract the CLR payload from
        // any IGH_Goo. Returning Value lets opaque inputs flow into a method
        // parameter typed as T with no further conversion.
        public override object ScriptVariable() => Value;

        public override bool CastFrom(object source)
        {
            if (source == null) return false;
            if (source is T tv) { Value = tv; return true; }
            if (source is GH_BettaGoo<T> g) { Value = g.Value; return true; }
            // GH_ObjectWrapper bag: a plain object on a generic wire.
            if (source is GH_ObjectWrapper w && w.Value is T wt) { Value = wt; return true; }
            return false;
        }

        public override bool CastTo<Q>(ref Q target)
        {
            if (Value is Q q) { target = q; return true; }
            return false;
        }

        // ---- IBettaSerializable round-trip --------------------------------
        //
        // If T implements IBettaSerializable AND has a parameterless ctor,
        // persist the bytes on Write and rebuild on Read. Otherwise both are
        // no-ops — the value is recomputed by re-solving from the wired
        // inputs at reload time.

        private const string SerializedKey = "betta_bytes";

        public override bool Write(GH_IWriter writer)
        {
            if (Value is IBettaSerializable serializable)
            {
                try
                {
                    var bytes = serializable.ToBytes();
                    if (bytes != null) writer.SetByteArray(SerializedKey, bytes);
                }
                catch { /* best-effort persistence — never break the .gh save */ }
            }
            return base.Write(writer);
        }

        public override bool Read(GH_IReader reader)
        {
            if (typeof(IBettaSerializable).IsAssignableFrom(typeof(T)) &&
                typeof(T).GetConstructor(Type.EmptyTypes) != null &&
                reader.ItemExists(SerializedKey))
            {
                try
                {
                    var bytes = reader.GetByteArray(SerializedKey);
                    var instance = (IBettaSerializable)Activator.CreateInstance(typeof(T));
                    instance.LoadFromBytes(bytes);
                    Value = (T)instance;
                }
                catch { /* fall back to default-constructed / null */ }
            }
            return base.Read(reader);
        }
    }
}
