using System.Numerics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace Rinku.Mapping.Emission;

internal readonly record struct EmissionFingerprint(ulong A, ulong B, ulong C, ulong D);

internal sealed class EmissionFingerprintBuilder {
    private ulong A = 0x243F6A8885A308D3UL;
    private ulong B = 0x13198A2E03707344UL;
    private ulong C = 0xA4093822299F31D0UL;
    private ulong D = 0x082EFA98EC4E6C89UL;

    internal EmissionFingerprint Value => new(A, B, C, D);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Add(ulong value) {
        A = Mix(A, value + 0x9E3779B97F4A7C15UL);
        B = Mix(B, value + 0xC2B2AE3D27D4EB4FUL);
        C = Mix(C, value + 0x165667B19E3779F9UL);
        D = Mix(D, value + 0x85EBCA77C2B2AE63UL);
    }

    internal void Add(OpCode opcode) => Add((ushort)opcode.Value);
    internal void Add(bool value) => Add(value ? 1UL : 0UL);
    internal void Add(byte value) => Add((ulong)value);
    internal void Add(short value) => Add(unchecked((ulong)value));
    internal void Add(int value) => Add(unchecked((ulong)value));
    internal void Add(long value) => Add(unchecked((ulong)value));
    internal void Add(float value) => Add(BitConverter.SingleToUInt32Bits(value));
    internal void Add(double value) => Add(unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

    internal void Add(string? value) {
        if (value is null) {
            Add(ulong.MaxValue);
            return;
        }
        Add(value.Length);
        foreach (char c in value)
            Add(c);
    }

    internal void Add(Type? type) {
        if (type is null) {
            Add(ulong.MaxValue - 1);
            return;
        }
        try {
            Add(unchecked((ulong)type.TypeHandle.Value.ToInt64()));
        }
        catch (NotSupportedException) {
            Add(unchecked((uint)RuntimeHelpers.GetHashCode(type)));
        }
    }

    internal void Add(Type[]? types) {
        if (types is null) {
            Add(ulong.MaxValue - 2);
            return;
        }
        Add(types.Length);
        foreach (var type in types)
            Add(type);
    }

    internal void Add(MethodBase method) {
        try {
            Add(unchecked((ulong)method.MethodHandle.Value.ToInt64()));
        }
        catch (NotSupportedException) {
            Add(unchecked((uint)RuntimeHelpers.GetHashCode(method)));
        }
    }

    internal void Add(FieldInfo field) {
        try {
            Add(unchecked((ulong)field.FieldHandle.Value.ToInt64()));
        }
        catch (NotSupportedException) {
            Add(unchecked((uint)RuntimeHelpers.GetHashCode(field)));
        }
    }

    internal void Add(SignatureHelper signature) => Add(unchecked((uint)RuntimeHelpers.GetHashCode(signature)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mix(ulong state, ulong value) {
        state ^= value;
        state *= 0x9E3779B185EBCA87UL;
        return BitOperations.RotateLeft(state, 27) * 5 + 0x52DCE729UL;
    }
}
