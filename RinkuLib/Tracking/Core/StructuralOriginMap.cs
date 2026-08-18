using System;
using System.Runtime.CompilerServices;

namespace Rinku.Tracking;

// Used only when T cannot tell TrackingList whether it has an original.
// Active items stay stored once in TrackingList; this keeps one provenance bit per row only
// when baseline and added rows are mixed. All-baseline and all-added states allocate nothing.
internal struct StructuralOriginMap {
    private enum OriginMode : byte { Baseline, Added, Mixed }

    private OriginMode _mode;
    private ulong[]? _bits;
    private int _mixedAddedCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly bool IsAdded(int index) => _mode switch {
        OriginMode.Baseline => false,
        OriginMode.Added => true,
        _ => GetBit(index)
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal readonly int AddedCount(int count) => _mode switch {
        OriginMode.Baseline => 0,
        OriginMode.Added => count,
        _ => _mixedAddedCount
    };

    internal void Insert(int index, int oldCount, bool added) {
        switch (_mode) {
            case OriginMode.Baseline:
                if (!added) return;
                if (oldCount == 0) { _mode = OriginMode.Added; return; }
                CreateMixed(oldCount + 1, setExisting: false);
                SetBit(index, true);
                _mixedAddedCount = 1;
                return;

            case OriginMode.Added:
                if (added) return;
                if (oldCount == 0) { _mode = OriginMode.Baseline; return; }
                CreateMixed(oldCount + 1, setExisting: true);
                SetBit(index, false);
                _mixedAddedCount = oldCount;
                return;

            default:
                EnsureWords(oldCount + 1);
                InsertBit(index, added, oldCount);
                if (added) _mixedAddedCount++;
                Normalize(oldCount + 1);
                return;
        }
    }

    internal void Remove(int index, int oldCount) {
        if (oldCount == 1) { Reset(); return; }
        if (_mode != OriginMode.Mixed) return;
        if (RemoveBit(index, oldCount)) _mixedAddedCount--;
        Normalize(oldCount - 1);
    }

    internal void Replace(int index, int count, bool added) {
        switch (_mode) {
            case OriginMode.Baseline:
                if (!added) return;
                if (count == 1) { _mode = OriginMode.Added; return; }
                CreateMixed(count, setExisting: false);
                SetBit(index, true);
                _mixedAddedCount = 1;
                return;

            case OriginMode.Added:
                if (added) return;
                if (count == 1) { _mode = OriginMode.Baseline; return; }
                CreateMixed(count, setExisting: true);
                SetBit(index, false);
                _mixedAddedCount = count - 1;
                return;

            default:
                bool previous = GetBit(index);
                if (previous == added) return;
                SetBit(index, added);
                _mixedAddedCount += added ? 1 : -1;
                Normalize(count);
                return;
        }
    }

    internal void Move(int oldIndex, int newIndex, int count) {
        if (_mode != OriginMode.Mixed || oldIndex == newIndex) return;
        bool added = RemoveBit(oldIndex, count);
        InsertBit(newIndex, added, count - 1);
    }

    internal void Reset() {
        _mode = OriginMode.Baseline;
        _bits = null;
        _mixedAddedCount = 0;
    }

    internal void Trim(int count) {
        if (_mode != OriginMode.Mixed || _bits is null) return;
        int words = WordCount(count);
        if (_bits.Length != words) Array.Resize(ref _bits, words);
    }

    private void Normalize(int count) {
        if (_mode != OriginMode.Mixed) return;
        if (_mixedAddedCount == 0) { Reset(); return; }
        if (_mixedAddedCount == count) {
            _mode = OriginMode.Added;
            _bits = null;
            _mixedAddedCount = 0;
        }
    }

    private void CreateMixed(int count, bool setExisting) {
        _mode = OriginMode.Mixed;
        _bits = new ulong[WordCount(count)];
        if (!setExisting) {
            _mixedAddedCount = 0;
            return;
        }
        Array.Fill(_bits, ulong.MaxValue);
        ClearUnused(count);
        _mixedAddedCount = count;
    }

    private void EnsureWords(int count) {
        int words = WordCount(count);
        if (_bits is null) _bits = new ulong[words];
        else if (_bits.Length < words) Array.Resize(ref _bits, words);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly bool GetBit(int index) => (_bits![index >> 6] & (1UL << (index & 63))) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetBit(int index, bool value) {
        ulong bit = 1UL << (index & 63);
        ref ulong word = ref _bits![index >> 6];
        if (value) word |= bit;
        else word &= ~bit;
    }

    private void InsertBit(int index, bool value, int oldCount) {
        int oldWords = WordCount(oldCount), newCount = oldCount + 1, newWords = WordCount(newCount);
        EnsureWords(newCount);
        int startWord = index >> 6, offset = index & 63;

        for (int word = newWords - 1; word > startWord; word--) {
            ulong current = word < oldWords ? _bits![word] : 0;
            ulong previous = _bits![word - 1];
            _bits[word] = (current << 1) | (previous >> 63);
        }

        ulong source = _bits![startWord];
        ulong lowMask = offset == 0 ? 0 : (1UL << offset) - 1;
        _bits[startWord] = (source & lowMask) | ((source & ~lowMask) << 1);
        SetBit(index, value);
        ClearUnused(newCount);
    }

    private bool RemoveBit(int index, int oldCount) {
        bool removed = GetBit(index);
        int startWord = index >> 6, offset = index & 63, lastWord = (oldCount - 1) >> 6;

        for (int word = startWord; word < lastWord; word++) {
            ulong current = _bits![word], next = _bits[word + 1];
            ulong shifted = (current >> 1) | (next << 63);
            if (word == startWord) {
                ulong lowMask = offset == 0 ? 0 : (1UL << offset) - 1;
                _bits[word] = (current & lowMask) | (shifted & ~lowMask);
            }
            else _bits[word] = shifted;
        }

        ulong last = _bits![lastWord], lastShifted = last >> 1;
        if (lastWord == startWord) {
            ulong lowMask = offset == 0 ? 0 : (1UL << offset) - 1;
            _bits[lastWord] = (last & lowMask) | (lastShifted & ~lowMask);
        }
        else _bits[lastWord] = lastShifted;

        ClearUnused(oldCount - 1);
        return removed;
    }

    private void ClearUnused(int count) {
        if (_bits is null || _bits.Length == 0) return;
        int words = WordCount(count);
        if (words == 0) { Array.Clear(_bits, 0, _bits.Length); return; }
        int remainder = count & 63;
        if (remainder != 0) _bits[words - 1] &= (1UL << remainder) - 1;
        for (int i = words; i < _bits.Length; i++) _bits[i] = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int WordCount(int count) => (count + 63) >> 6;
}
