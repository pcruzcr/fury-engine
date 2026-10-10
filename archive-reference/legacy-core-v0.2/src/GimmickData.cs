// Fighter.Core — per-fighter gimmick state blob (REFACTOR+HARDEN, schema 5).
// 128-byte fixed-size blittable struct, zero-padded, versioned with a byte
// tag. Today every fighter's blob is all-zero (reserved): no gimmick writes
// any byte yet, so the schema-5 payload change is hash-stable in shape but
// NOT in value (payload grows; hashes change vs schema 4 — no golden pins
// exist, all tests compare run-vs-run). Gimmick authors claim bytes via the
// tag registry below; unclaimed bytes must stay zero (enforced by review,
// not by runtime — the sim never branches on unclaimed bytes).
namespace Fighter.Core;

/// <summary>Per-fighter gimmick state: 128 bytes, blittable, zero-padded.</summary>
/// <remarks>Explicit layout, <see cref="Size"/> bytes. Byte 0 is the
/// <see cref="Tag"/> (0 = empty/reserved); bytes 1..7 are reserved padding
/// (must stay zero); bytes 8..127 are fifteen payload words W00..W14.
/// IL2CPP-safe: value type only, no references, no generics.</remarks>
[System.Runtime.InteropServices.StructLayout(
    System.Runtime.InteropServices.LayoutKind.Explicit, Size = 128)]
public struct GimmickData : System.IEquatable<GimmickData>
{
    /// <summary>Total size in bytes (snapshot budget accounting).</summary>
    public const int Size = 128;
    /// <summary>Payload words (15 x 8 bytes at offsets 8..127).</summary>
    public const int PayloadWords = 15;

    /// <summary>Version tag at byte 0. 0 = empty/reserved.</summary>
    [System.Runtime.InteropServices.FieldOffset(0)]
    public byte Tag;

    // Visible instance fields are intentional: this blittable snapshot struct's
    // fields ARE the wire format (explicit layout). No analyzer pack is
    // referenced, so no CA1051 fires; TreatWarningsAsErrors still holds.
    /// <summary>Payload word 0 (bytes 8..15).</summary>
    [System.Runtime.InteropServices.FieldOffset(8)] public ulong W00;
    /// <summary>Payload word 1.</summary>
    [System.Runtime.InteropServices.FieldOffset(16)] public ulong W01;
    /// <summary>Payload word 2.</summary>
    [System.Runtime.InteropServices.FieldOffset(24)] public ulong W02;
    /// <summary>Payload word 3.</summary>
    [System.Runtime.InteropServices.FieldOffset(32)] public ulong W03;
    /// <summary>Payload word 4.</summary>
    [System.Runtime.InteropServices.FieldOffset(40)] public ulong W04;
    /// <summary>Payload word 5.</summary>
    [System.Runtime.InteropServices.FieldOffset(48)] public ulong W05;
    /// <summary>Payload word 6.</summary>
    [System.Runtime.InteropServices.FieldOffset(56)] public ulong W06;
    /// <summary>Payload word 7.</summary>
    [System.Runtime.InteropServices.FieldOffset(64)] public ulong W07;
    /// <summary>Payload word 8.</summary>
    [System.Runtime.InteropServices.FieldOffset(72)] public ulong W08;
    /// <summary>Payload word 9.</summary>
    [System.Runtime.InteropServices.FieldOffset(80)] public ulong W09;
    /// <summary>Payload word 10.</summary>
    [System.Runtime.InteropServices.FieldOffset(88)] public ulong W10;
    /// <summary>Payload word 11.</summary>
    [System.Runtime.InteropServices.FieldOffset(96)] public ulong W11;
    /// <summary>Payload word 12.</summary>
    [System.Runtime.InteropServices.FieldOffset(104)] public ulong W12;
    /// <summary>Payload word 13.</summary>
    [System.Runtime.InteropServices.FieldOffset(112)] public ulong W13;
    /// <summary>Payload word 14 (bytes 120..127).</summary>
    [System.Runtime.InteropServices.FieldOffset(120)] public ulong W14;

    /// <summary>True when every byte is zero (nothing claimed).</summary>
    /// <returns>True for a default-constructed blob.</returns>
    public readonly bool IsEmpty()
    {
        if (Tag != 0) return false;
        return W00 == 0 && W01 == 0 && W02 == 0 && W03 == 0 && W04 == 0 &&
               W05 == 0 && W06 == 0 && W07 == 0 && W08 == 0 && W09 == 0 &&
               W10 == 0 && W11 == 0 && W12 == 0 && W13 == 0 && W14 == 0;
    }

    /// <summary>Reads payload word <paramref name="index"/> (0..14).</summary>
    /// <param name="index">Word index.</param>
    /// <returns>Payload word.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when out of range.</exception>
    public readonly ulong GetWord(int index) => index switch
    {
        0 => W00, 1 => W01, 2 => W02, 3 => W03, 4 => W04,
        5 => W05, 6 => W06, 7 => W07, 8 => W08, 9 => W09,
        10 => W10, 11 => W11, 12 => W12, 13 => W13, 14 => W14,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    /// <summary>Writes payload word <paramref name="index"/> (0..14).</summary>
    /// <param name="index">Word index.</param>
    /// <param name="value">Word value.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when out of range.</exception>
    public void SetWord(int index, ulong value)
    {
        switch (index)
        {
            case 0: W00 = value; break;
            case 1: W01 = value; break;
            case 2: W02 = value; break;
            case 3: W03 = value; break;
            case 4: W04 = value; break;
            case 5: W05 = value; break;
            case 6: W06 = value; break;
            case 7: W07 = value; break;
            case 8: W08 = value; break;
            case 9: W09 = value; break;
            case 10: W10 = value; break;
            case 11: W11 = value; break;
            case 12: W12 = value; break;
            case 13: W13 = value; break;
            case 14: W14 = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>Structural equality (every byte).</summary>
    /// <param name="other">Value to compare.</param>
    /// <returns>True when all words and the tag match.</returns>
    public readonly bool Equals(GimmickData other) =>
        Tag == other.Tag && W00 == other.W00 && W01 == other.W01 && W02 == other.W02 &&
        W03 == other.W03 && W04 == other.W04 && W05 == other.W05 && W06 == other.W06 &&
        W07 == other.W07 && W08 == other.W08 && W09 == other.W09 && W10 == other.W10 &&
        W11 == other.W11 && W12 == other.W12 && W13 == other.W13 && W14 == other.W14;

    /// <summary>Type-checked equality.</summary>
    /// <param name="obj">Value to compare.</param>
    /// <returns>True when <paramref name="obj"/> is an equal blob.</returns>
    public override readonly bool Equals(object? obj) => obj is GimmickData g && Equals(g);

    /// <summary>Combined hash (tag + words).</summary>
    /// <returns>Combined hash code.</returns>
    public override readonly int GetHashCode()
    {
        int h = System.HashCode.Combine(Tag, W00, W01, W02, W03, W04, W05, W06);
        h = System.HashCode.Combine(h, W07, W08, W09, W10, W11, W12, W13);
        return System.HashCode.Combine(h, W14);
    }
}
