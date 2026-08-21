using System.Runtime.CompilerServices;

namespace Rinku.Tracking;

internal struct StructuralOriginMap
{
    private enum OriginMode : byte { Baseline, Added, Mixed }

    private OriginMode _mode;
    private ulong[]? _bits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly bool IsAdded(int index)
        => _mode switch
        {
            OriginMode.Baseline => false,
            OriginMode.Added => true,
            _ => GetBit(index)
        };

    internal void Insert(int index, int oldCount, bool added)
    {
        switch (_mode)
        {
            case OriginMode.Baseline:
                if (!added) return;
                if (oldCount == 0)
                {
                    _mode = OriginMode.Added;
                    return;
                }
                CreateMixed(oldCount + 1, setExisting: false);
                SetBit(index, true);
                return;

            case OriginMode.Added:
                if (added) return;
                if (oldCount == 0)
                {
                    _mode = OriginMode.Baseline;
                    return;
                }
                CreateMixed(oldCount + 1, setExisting: true);
                SetBit(index, false);
                return;

            default:
                EnsureWords(oldCount + 1);
                InsertBit(index, added, oldCount);
                return;
        }
    }

    internal void Remove(int index, int oldCount)
    {
        if (oldCount == 1)
        {
            Reset();
            return;
        }

        if (_mode == OriginMode.Mixed)
            RemoveBit(index, oldCount);
    }

    internal void Replace(int index, int count, bool added)
    {
        switch (_mode)
        {
            case OriginMode.Baseline:
                if (!added) return;
                if (count == 1)
                {
                    _mode = OriginMode.Added;
                    return;
                }
                CreateMixed(count, setExisting: false);
                SetBit(index, true);
                return;

            case OriginMode.Added:
                if (added) return;
                if (count == 1)
                {
                    _mode = OriginMode.Baseline;
                    return;
                }
                CreateMixed(count, setExisting: true);
                SetBit(index, false);
                return;

            default:
                SetBit(index, added);
                return;
        }
    }

    internal void Move(int oldIndex, int newIndex, int count)
    {
        if (_mode != OriginMode.Mixed || oldIndex == newIndex) return;
        bool added = RemoveBit(oldIndex, count);
        InsertBit(newIndex, added, count - 1);
    }

    internal void Reset()
    {
        _mode = OriginMode.Baseline;
        _bits = null;
    }

    internal void Trim(int count)
    {
        if (_mode != OriginMode.Mixed || _bits is null) return;
        int words = WordCount(count);
        if (_bits.Length != words) Array.Resize(ref _bits, words);
    }

    private void CreateMixed(int count, bool setExisting)
    {
        _mode = OriginMode.Mixed;
        _bits = new ulong[WordCount(count)];
        if (!setExisting) return;
        Array.Fill(_bits, ulong.MaxValue);
        ClearUnused(count);
    }

    private void EnsureWords(int count)
    {
        int words = WordCount(count);
        if (_bits is null)
            _bits = new ulong[words];
        else if (_bits.Length < words)
            Array.Resize(ref _bits, words);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly bool GetBit(int index)
    {
        ulong[] bits = _bits ?? throw new InvalidOperationException("Mixed provenance has no bit storage.");
        return (bits[index >> 6] & (1UL << (index & 63))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBit(int index, bool value)
    {
        ulong[] bits = _bits ?? throw new InvalidOperationException("Mixed provenance has no bit storage.");
        ulong bit = 1UL << (index & 63);
        ref ulong word = ref bits[index >> 6];
        if (value) word |= bit;
        else word &= ~bit;
    }

    private void InsertBit(int index, bool value, int oldCount)
    {
        int oldWords = WordCount(oldCount);
        int newCount = oldCount + 1;
        int newWords = WordCount(newCount);
        EnsureWords(newCount);
        ulong[] bits = _bits ?? throw new InvalidOperationException("Mixed provenance has no bit storage.");
        int startWord = index >> 6;
        int offset = index & 63;

        for (int word = newWords - 1; word > startWord; word--)
        {
            ulong current = word < oldWords ? bits[word] : 0;
            bits[word] = (current << 1) | (bits[word - 1] >> 63);
        }

        ulong source = bits[startWord];
        ulong lowMask = offset == 0 ? 0 : (1UL << offset) - 1;
        bits[startWord] = (source & lowMask) | ((source & ~lowMask) << 1);
        SetBit(index, value);
        ClearUnused(newCount);
    }

    private bool RemoveBit(int index, int oldCount)
    {
        ulong[] bits = _bits ?? throw new InvalidOperationException("Mixed provenance has no bit storage.");
        bool removed = GetBit(index);
        int startWord = index >> 6;
        int offset = index & 63;
        int lastWord = (oldCount - 1) >> 6;

        for (int word = startWord; word < lastWord; word++)
        {
            ulong current = bits[word];
            ulong shifted = (current >> 1) | (bits[word + 1] << 63);
            if (word == startWord)
            {
                ulong lowMask = offset == 0 ? 0 : (1UL << offset) - 1;
                bits[word] = (current & lowMask) | (shifted & ~lowMask);
            }
            else
            {
                bits[word] = shifted;
            }
        }

        ulong last = bits[lastWord];
        ulong lastShifted = last >> 1;
        if (lastWord == startWord)
        {
            ulong lowMask = offset == 0 ? 0 : (1UL << offset) - 1;
            bits[lastWord] = (last & lowMask) | (lastShifted & ~lowMask);
        }
        else
        {
            bits[lastWord] = lastShifted;
        }

        ClearUnused(oldCount - 1);
        return removed;
    }

    private void ClearUnused(int count)
    {
        if (_bits is not ulong[] bits || bits.Length == 0) return;
        int words = WordCount(count);
        if (words == 0)
        {
            Array.Clear(bits, 0, bits.Length);
            return;
        }

        int remainder = count & 63;
        if (remainder != 0) bits[words - 1] &= (1UL << remainder) - 1;
        for (int i = words; i < bits.Length; i++) bits[i] = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WordCount(int count) => (count + 63) >> 6;
}
