using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Rinku.Internal;

namespace Rinku.Querying;

[Flags]
internal enum CondFlags : byte {
    None = 0,
    IsNot = 0b_0000_0100,
    NeedSectionToFinish = 0b_0000_1000,
    IsRequired = 0b_0001_0000,
    NextIsSection = 0b_0100_0000,
    Finished = 0b_1000_0000,
}
internal struct CondInfo {
    public const char NotCommentChar = '!';
    public const char AndComment = (char)1;
    public const char AndCommentChar = '&';
    public const char OrComment = (char)2;
    public const char OrCommentChar = '|';
    public const char None = (char)3;
    public const char Special = (char)8;
    public static bool IsComment(char type) => type <= OrComment;
    public string Cond { get; private set; }
    public int StartIndex { get; private set; }
    public int VarIndex { get; private set; }
    public char Type { get; private set; }
    public ulong ParMapOrExcesses { get; private set; }
    public CondFlags Flags { get; set; }
    public int EndIndex { get; private set; }
    public static CondInfo NewRequired(string Cond, char Type, int VarIndex)
        => new() {
            Cond = Cond,
            Type = Type,
            VarIndex = VarIndex,
            Flags = CondFlags.IsRequired | CondFlags.Finished
        };
    public static CondInfo NewOptional(string Cond, char Type, int VarIndex, int StartIndex, ulong ParMap, int Excess, bool IsNot)
        => new() {
            Cond = Cond,
            Type = Type,
            VarIndex = VarIndex,
            StartIndex = StartIndex,
            ParMapOrExcesses = ParMap,
            EndIndex = Excess,
            Flags = IsNot ? CondFlags.IsNot : default
        };
    public static CondInfo NewSelect(int StartIndex, ulong parMap, int prevExcessExcess) 
        =>new() {
               Cond = null!,
               Type = CondInfo.AndComment,
               VarIndex = -1,
               StartIndex = StartIndex,
               ParMapOrExcesses = parMap,
               EndIndex = prevExcessExcess
        };
    public void Finish(int endIndex, bool nextIsSection) {
        ParMapOrExcesses = (ulong)EndIndex;
        Flags |= CondFlags.Finished;
        if (nextIsSection)
            Flags |= CondFlags.NextIsSection;
        EndIndex = endIndex;
    }
    public override readonly string ToString()
        => $"{Cond}, {(Type < (char)32 ? (object)(int)Type : Type)}, {StartIndex}, {EndIndex}";
    public void UpdateSelectCond(string cond, int currentStart, int prevSegExcess) {
        if (StartIndex < 0)
            StartIndex = currentStart;
        if (EndIndex <= 0)
            EndIndex = prevSegExcess;
        Cond = cond;
    }
    public void UpdateCommentAsSectionComment(int StartInd) {
        Flags |= CondFlags.NeedSectionToFinish;
        EndIndex = 0;
        StartIndex = StartInd;

    }
    public void UpdateNestingLevel(ulong parMap) => ParMapOrExcesses = parMap;
    public void SetType(char type) => Type = type;
    public readonly bool IsFinished => Flags.HasFlag(CondFlags.Finished);
    public readonly bool IsRequired => Flags.HasFlag(CondFlags.IsRequired);
    public readonly bool NeedSectionToFinish => Flags.HasFlag(CondFlags.NeedSectionToFinish);
    public readonly bool NextSegmentIsSection => Flags.HasFlag(CondFlags.NextIsSection);
    public readonly int PrevSegmentExcess => (int)(uint)ParMapOrExcesses;
}
internal unsafe ref struct QueryExtractor {
    public const char OptionalVariableIdentifier = '?';
    public const char JoinAndOrChar = '&';
    public const char SelectColumnAlwaysUsed = '!';
    public const char CommentAsCommentChar = '~';
#pragma warning disable CA2211
    public static char HandlerChar = '_';
#pragma warning restore CA2211
    private int Length;
    private char* CurrentChar;
    private char* LastChar;
    private char[] Builder;
    private Span<char> BuilderSpan;
    private int BuilderInd;
    private int CurrentQuote;
    private int* CurrentStart;
    private int* CurrentExcess;
    private int LastUnfinishedSection;
    private ulong ParMap;
    private void UpdateCurrentStart(int newStart, int newExcess) {
        *CurrentStart = newStart;
        *CurrentExcess = newExcess;
    }
    private bool PrevBoundary;
    private bool ContainingParantesis;
    private ulong SelectExtractionParMap;
    private PooledArray<CondInfo> Conditions;
    private uint LastCondSectionLength;
    internal static PooledArray<CondInfo>.Locked Segment(string query, char variableChar, out string newQuery) {
        var seg = new QueryExtractor();
        return seg.SegmentQuery(query, variableChar, out newQuery);
    }
    private PooledArray<CondInfo>.Locked SegmentQuery(string query, char variableChar, out string newQuery) {
        Length = query.Length;
        if (Length <= 1)
            throw new RinkuTemplateException(ErrorCodes.QueryTooShort, $"invalid query \"{query}\", must contains at least 2 letters");
        Conditions = new PooledArray<CondInfo>();
        Builder = ArrayPool<char>.Shared.Rent((int)(Length * 1.1));
        BuilderSpan = Builder;
        BuilderInd = 0;
        CurrentQuote = 0;
        PrevBoundary = true;
        var startIndexes = ArrayPool<int>.Shared.Rent(64);
        var excesses = ArrayPool<int>.Shared.Rent(64);
        ParMap = 1;

        fixed (int* ps = &MemoryMarshal.GetReference(startIndexes.AsSpan()))
        fixed (int* pe = &MemoryMarshal.GetReference(excesses.AsSpan()))
        fixed (char* p = &MemoryMarshal.GetReference(query.AsSpan())) {
            CurrentChar = p;
            CurrentStart = ps;
            CurrentExcess = pe;
            LastChar = p + Length;
            for (; CurrentChar < LastChar; CurrentChar++) {
                Builder[BuilderInd++] = *CurrentChar;
                if (CurrentQuote == 0 && PrevBoundary && *CurrentChar == '$' && TryManageDollarQuote())
                    continue;
                if (IsBoundary(*CurrentChar)) {
                    ManageBoundary();
                    continue;
                }
                if (CurrentQuote == 0 && *CurrentChar == '-' && CurrentChar[1] == '-') {
                    ManageLineComment();
                    continue;
                }
                if (*CurrentChar == OptionalVariableIdentifier && CurrentChar[1] != variableChar) {
                    if (CurrentChar[1] == OptionalVariableIdentifier
                        && CurrentChar[2] == OptionalVariableIdentifier) {
                        BuilderInd--;
                        UpdateConditionsEnd(BuilderInd, false, 0, keepUnstartedColumn: true);
                        UpdateCurrentStart(BuilderInd, 0);
                        CurrentChar += 2;
                    }
                    continue;
                }
                if (!PrevBoundary || CurrentQuote != 0)
                    continue;
                PrevBoundary = false;
                if (*CurrentChar == variableChar && CurrentChar[1] == variableChar) {
                    Builder[BuilderInd++] = CurrentChar[1];
                    CurrentChar++;
                    continue;
                }
                if (TryManageVariable(variableChar)) { }
                else if (*CurrentChar == JoinAndOrChar) {
                    if (IsOr(CurrentChar + 1) || IsAnd(CurrentChar + 1))
                        BuilderInd--;
                }
                else if (IsOr(CurrentChar)) {
                    UpdateConditionsEnd(BuilderInd + 1, false, 2);
                    UpdateCurrentStart(BuilderInd + 1, 2);
                }
                else if (IsAnd(CurrentChar)) {
                    UpdateConditionsEnd(BuilderInd + 2, false, 3);
                    UpdateCurrentStart(BuilderInd + 2, 3);
                }
                else if (IsOn(CurrentChar))
                    UpdateCurrentStart(BuilderInd + 1, 0);
                else if (IsEnd(CurrentChar))
                    LowerParentesis();
                else if (IsCaseOrBegin(CurrentChar)) {
                    RaiseParentesis(false);
                    ParMap |= 1;
                    CurrentChar++;
                    Builder[BuilderInd++] = *CurrentChar++;
                    Builder[BuilderInd++] = *CurrentChar++;
                    Builder[BuilderInd++] = *CurrentChar;
                }
                else
                    TryManageSection();
            }
            UpdateConditionsEnd(BuilderInd, true, 0);
        }
        CurrentChar = null;
        ArrayPool<int>.Shared.Return(startIndexes);
        ArrayPool<int>.Shared.Return(excesses);
        CurrentStart = null;
        CurrentExcess = null;
        newQuery = new string(Builder, 0, BuilderInd);
        ArrayPool<char>.Shared.Return(Builder);
        Builder = null!;
        return Conditions.LockTransfer();
    }

    private bool TryManageSection() {
        var secLen = (int)(LastCondSectionLength & 0xFF);
        if (secLen <= 0 && !MatchSection(CurrentChar, out secLen))
            return false;
        var isDynamicProjection = BuilderInd > 1 && Builder[BuilderInd - 2] == OptionalVariableIdentifier && secLen == 6 && IsSelect(CurrentChar);
        if (isDynamicProjection) {
            BuilderInd--;
            Builder[BuilderInd - 1] = *CurrentChar;
        }
        var needSpace = BuilderInd > 1 && !char.IsWhiteSpace(Builder[BuilderInd - 2]);
        var endInd = BuilderInd - 2;
        if (needSpace)
            endInd++;
        else
            while (endInd > 0 && char.IsWhiteSpace(Builder[endInd - 1]))
                endInd--;
        if ((secLen == 6 || secLen == 11) && (IsInsert(CurrentChar) || IsValues(CurrentChar)))
            ContainingParantesis = true;
        else if (ContainingParantesis && ParMap == 1)
            ContainingParantesis = false;
        if (ParMap == SelectExtractionParMap)
            SelectExtractionParMap = 0;
        if (UpdateConditionsEnd(endInd, secLen > 0, 0) && needSpace) {
            Builder[BuilderInd - 1] = ' ';
            Builder[BuilderInd] = *CurrentChar;
            BuilderInd++;
        }
        UpdateCurrentStart(BuilderInd + secLen - 1, secLen);

        for (int i = 1; i < secLen; i++) {
            CurrentChar++;
            Builder[BuilderInd++] = *CurrentChar;
        }
        if (isDynamicProjection) {
            SelectExtractionParMap = ParMap;
            Conditions.Add(CondInfo.NewSelect(-1, ParMap, 0));
        }
        return true;
    }

    private const ulong BoundaryMask = 0x800938500002601;
    private static bool IsBoundary(char c)
        => c < 64 ? (BoundaryMask >> c & 1) == 1 : c == '[' || c == ']' || c == '`';
    private void ManageBoundary() {
        var c = *CurrentChar;
        PrevBoundary = true;
        if (ManageQuote(c))
            return;
        if (TryManageComment(true)) {
            CurrentChar--;
            return;
        }
        if (c == '(')
            RaiseParentesis(true);
        else if (c == ')')
            LowerParentesis();
        else if (c == ',')
            ManageComa();
        else if (c == ';') {
            UpdateConditionsEnd(BuilderInd - 1, true, 0);
            UpdateCurrentStart(BuilderInd, 0);
        }
    }

    private void ManageComa() {
        if (CurrentChar[-1] == JoinAndOrChar) {
            BuilderInd--;
            Builder[BuilderInd - 1] = ',';
            return;
        }
        if (CurrentChar[-1] == SelectColumnAlwaysUsed) {
            BuilderInd--;
            Builder[BuilderInd - 1] = ',';
            int i = Conditions.Length - 1;
            for (; i >= LastUnfinishedSection; i--) {
                ref var cond = ref Conditions[i];
                if (cond.IsFinished || cond.ParMapOrExcesses != ParMap || cond.NeedSectionToFinish || cond.Cond is not null)
                    continue;
                break;
            }
            if (i < LastUnfinishedSection)
                throw new RinkuTemplateException(ErrorCodes.ProjectionOnlyConstruct, $"The {SelectColumnAlwaysUsed} may only be used in a dynamic projection context {new string(Builder.AsSpan(0, BuilderInd))}");
            Conditions.RemoveAt(i);
            if (i == LastUnfinishedSection) {
                for (; i < Conditions.Length; i++)
                    if (!Conditions[i].IsFinished)
                        break;
                LastUnfinishedSection = i;
            }
        }
        UpdateConditionsEnd(BuilderInd, false, 1);
        UpdateCurrentStart(BuilderInd, 1);
        if (SelectExtractionParMap == ParMap) Conditions.Add(CondInfo.NewSelect(BuilderInd, ParMap, 1));
    }

    private bool TryManageVariable(char variableChar) {
        var isRequired = !(*CurrentChar == OptionalVariableIdentifier && CurrentChar[1] == variableChar);
        if (isRequired && *CurrentChar != variableChar)
            return false;
        var varIndex = BuilderInd - 1;
        if (!isRequired) {
            Builder[varIndex] = variableChar;
            CurrentChar++;
        }
        CurrentChar++;
        while (!IsBoundary(*CurrentChar) && *CurrentChar != JoinAndOrChar
            && !(*CurrentChar == '-' && CurrentChar[1] == '-')) {
            Builder[BuilderInd++] = *CurrentChar;
            CurrentChar++;
        }
        var type = CondInfo.None;
        var varLength = BuilderInd - varIndex;
        if (*(CurrentChar - 2) == HandlerChar) {
            type = *(CurrentChar - 1);
            varLength -= 2;
        }
        var cond = new string(Builder, varIndex, varLength);
        if (isRequired) {
            Conditions.Add(CondInfo.NewRequired(cond, type, varIndex));
        } else {
            var decal = GetDecalToSectionLevel(ParMap);
            Conditions.Add(CondInfo.NewOptional(cond, type, varIndex, CurrentStart[-decal], ParMap >> decal, CurrentExcess[-decal], false));
        }
        if (type >= CondInfo.Special) {
            var c = CurrentChar;
            while (char.IsWhiteSpace(*c))
                c++;
        }
        CurrentChar--;
        return true;
    }
    private bool TryManageComment(bool currentCharAddedToBuilder, int minStart = 0) {
        if (*CurrentChar != '/' || CurrentChar[1] != '*')
            return false;
        if (currentCharAddedToBuilder)
            BuilderInd--;
        CurrentChar += 2;
        if (*CurrentChar == CommentAsCommentChar) {
            Builder[BuilderInd++] = '/';
            Builder[BuilderInd++] = '*';
            CurrentChar++;
            while (!(*CurrentChar == '*' && CurrentChar[1] == '/') && CurrentChar < LastChar)
                Builder[BuilderInd++] = *CurrentChar++;
            if (CurrentChar >= LastChar)
                throw new RinkuTemplateException(ErrorCodes.UnclosedComment, "comment unclosed");
            CurrentChar++;
            Builder[BuilderInd++] = '*';
            Builder[BuilderInd++] = '/';
            return false;
        }
        var type = CondInfo.AndComment;
        var nbCond = 0;
        int firstCond = Conditions.Length;
        int ind;
        while (true) {
            var cond = GetCommentString(out var isNot);
            if (string.IsNullOrWhiteSpace(cond))
                throw new RinkuTemplateException(ErrorCodes.EmptyConditionKey, $"Cannot have a whitespace condition {new string(Builder)}");
            nbCond++;
            ind = BuilderInd - 1;
            if (ind < 0)
                ind = 0;
            Conditions.Add(CondInfo.NewOptional(cond, type, ind, *CurrentStart, ParMap, *CurrentExcess, isNot));
            if ((*CurrentChar == '*' && CurrentChar[1] == '/') || CurrentChar >= LastChar)
                break;
            type = *CurrentChar == CondInfo.OrCommentChar ? CondInfo.OrComment : CondInfo.AndComment;
            CurrentChar++;
        }
        nbCond = RewriteMarkerLeftToRight(firstCond, nbCond);
        CurrentChar += 2;
        SkipWhiteSpace();
        Debug.Assert(nbCond > 0, "the marker loop always collects at least one condition");
        if (MatchSection(CurrentChar, out var secLen)) { } else if (*CurrentChar == OptionalVariableIdentifier && IsSelect(CurrentChar + 1)) {
            secLen = 6;
        } else {
            return true;
        }
        LastCondSectionLength = (uint)nbCond << 16 | (uint)secLen;
        ind = BuilderInd - 1;
        while (ind > 0 && char.IsWhiteSpace(Builder[ind - 1]) && char.IsWhiteSpace(Builder[ind]))
            ind--;
        if (ind < minStart)
            ind = minStart;
        if (ind < 0)
            ind = 0;
        for (; nbCond > 0; nbCond--) {
            Conditions[^nbCond].UpdateCommentAsSectionComment(ind);
        }
        return true;
    }
    private int RewriteMarkerLeftToRight(int firstCond, int count) {
        if (count < 3)
            return count;
        var groups = new List<List<CondInfo>> { new() { Conditions[firstCond] } };
        for (int i = 1; i < count; i++) {
            var condition = Conditions[firstCond + i];
            if (condition.Type == CondInfo.OrComment) {
                for (int group = 0; group < groups.Count; group++)
                    groups[group].Add(condition);
                continue;
            }
            groups.Add([condition]);
        }
        int expandedCount = 0;
        for (int group = 0; group < groups.Count; group++)
            expandedCount += groups[group].Count;
        if (expandedCount == count)
            return count;
        for (int i = 0; i < count; i++)
            Conditions.RemoveAt(Conditions.Length - 1);
        for (int group = 0; group < groups.Count; group++) {
            var conditions = groups[group];
            for (int i = 0; i < conditions.Count; i++) {
                var condition = conditions[i];
                condition.SetType(i == 0 ? CondInfo.AndComment : CondInfo.OrComment);
                Conditions.Add(condition);
            }
        }
        return expandedCount;
    }
    private void ManageLineComment() {
        while (CurrentChar + 1 < LastChar && CurrentChar[1] != '\n' && CurrentChar[1] != '\r')
            Builder[BuilderInd++] = *++CurrentChar;
    }
    private void SkipWhiteSpace() {
        while (char.IsWhiteSpace(*CurrentChar)) {
            Builder[BuilderInd++] = *CurrentChar;
            CurrentChar++;
        }
    }
    private string GetCommentString(out bool isNot) {
        var start = CurrentChar;
        while (char.IsWhiteSpace(*start))
            start++;
        CurrentChar = start;
        while (CurrentChar < LastChar) {
            if ((*CurrentChar == '*' && CurrentChar[1] == '/')
                || *CurrentChar == '|'
                || *CurrentChar == '&')
                break;
            CurrentChar++;
        }
        if (CurrentChar >= LastChar)
            throw new RinkuTemplateException(ErrorCodes.UnclosedComment, "comment unclosed");
        isNot = *start == CondInfo.NotCommentChar;
        if (isNot)
            start++;
        var i = (int)(CurrentChar - start);
        while (i > 0 && char.IsWhiteSpace(start[i - 1]))
            i--;
        return new string(start, 0, i);
    }
    private bool ManageQuote(char c) {
        if (CurrentQuote != 0) {
            if (c == CurrentQuote)
                CurrentQuote = 0;
            return true;
        }
        if (c == '[') {
            CurrentQuote = ']';
            return true;
        }
        if (c == '\'' || c == '"' || c == '`') {
            CurrentQuote = c;
            return true;
        }
        return false;
    }
    private bool TryManageDollarQuote() {
        var delimiterEnd = CurrentChar + 1;
        if (delimiterEnd >= LastChar)
            return false;
        if (*delimiterEnd != '$') {
            if (!IsDollarTagStart(*delimiterEnd))
                return false;
            do delimiterEnd++; while (delimiterEnd < LastChar && IsDollarTagPart(*delimiterEnd));
            if (delimiterEnd >= LastChar || *delimiterEnd != '$')
                return false;
        }
        int delimiterLength = (int)(delimiterEnd - CurrentChar) + 1;
        var closing = delimiterEnd + 1;
        while (LastChar - closing >= delimiterLength) {
            if (*closing == '$' && DollarDelimiterMatches(CurrentChar, closing, delimiterLength)) {
                var closingEnd = closing + delimiterLength - 1;
                for (var source = CurrentChar + 1; source <= closingEnd; source++)
                    Builder[BuilderInd++] = *source;
                CurrentChar = closingEnd;
                PrevBoundary = false;
                return true;
            }
            closing++;
        }
        return false;
    }
    private static bool DollarDelimiterMatches(char* opening, char* closing, int length) {
        for (int i = 1; i < length; i++)
            if (opening[i] != closing[i])
                return false;
        return true;
    }
    private static bool IsDollarTagStart(char c) => c == '_' || char.IsLetter(c);
    private static bool IsDollarTagPart(char c) => c == '_' || char.IsLetterOrDigit(c);
    private void RaiseParentesis(bool checkSection) {
        if (ParMap >= 0x8000000000000000UL)
            throw new RinkuTemplateException(ErrorCodes.ScopeTooDeep, "cannot have more than 63 level deep of parentesis / cases");
        CurrentStart++;
        CurrentExcess++;
        UpdateCurrentStart(BuilderInd, 0);
        ParMap <<= 1;
        if (!checkSection)
            return;
        var afterParenthesis = BuilderInd;
        CurrentChar++;
        SkipWhiteSpace();
        if (TryManageComment(false, afterParenthesis))
            if (LastCondSectionLength > 0) {
                ParMap |= 1;
                for (int i = (int)(LastCondSectionLength >> 16); i > 0; i--)
                    Conditions[^i].UpdateNestingLevel(ParMap);
            }
        if (MatchSection(CurrentChar, out _) || (ParMap == 0b10 && ContainingParantesis))
            ParMap |= 1;
        CurrentChar--;
    }
    private void LowerParentesis() {
        UpdateConditionsEnd(BuilderInd - 1, true, 0);
        if (ParMap == 1)
            throw new RinkuTemplateException(ErrorCodes.UnbalancedScope, "too many closing parentesis / cases");
        ParMap >>= 1;
        CurrentStart--;
        CurrentExcess--;
    }
    private static int GetDecalToSectionLevel(ulong parMap) {
        int i = 0;
        while ((parMap & 1) == 0) {
            parMap >>= 1;
            i++;
        }
        return i;
    }
    private static bool IsInsert(char* ptr)
        => (*ptr | 0x20) == 'i' && (ptr[1] | 0x20) == 'n' && (ptr[2] | 0x20) == 's'
        && (ptr[3] | 0x20) == 'e' && (ptr[4] | 0x20) == 'r' && (ptr[5] | 0x20) == 't';
    private static bool IsSelect(char* ptr)
        => (*ptr | 0x20) == 's' && (ptr[1] | 0x20) == 'e' && (ptr[2] | 0x20) == 'l'
        && (ptr[3] | 0x20) == 'e' && (ptr[4] | 0x20) == 'c' && (ptr[5] | 0x20) == 't';
    private static bool IsValues(char* ptr)
        => (*ptr | 0x20) == 'v' && (ptr[1] | 0x20) == 'a' && (ptr[2] | 0x20) == 'l'
        && (ptr[3] | 0x20) == 'u' && (ptr[4] | 0x20) == 'e' && (ptr[5] | 0x20) == 's';
    private static bool IsCaseOrBegin(char* ptr)
        => ((*ptr | 0x20) == 'c' && (ptr[1] | 0x20) == 'a' && (ptr[2] | 0x20) == 's' && (ptr[3] | 0x20) == 'e')
        || ((*ptr | 0x20) == 'b' && (ptr[1] | 0x20) == 'e' && (ptr[2] | 0x20) == 'g' && (ptr[3] | 0x20) == 'i' && (ptr[4] | 0x20) == 'n');
    private static bool IsEnd(char* ptr)
        => (*ptr | 0x20) == 'e' && (ptr[1] | 0x20) == 'n' && (ptr[2] | 0x20) == 'd' && IsBoundary(ptr[3]);
    private static bool IsOr(char* ptr)
        => (*ptr | 0x20) == 'o' && (ptr[1] | 0x20) == 'r' && IsBoundary(ptr[2]);
    private static bool IsAnd(char* ptr)
        => (*ptr | 0x20) == 'a' && (ptr[1] | 0x20) == 'n' && (ptr[2] | 0x20) == 'd' && IsBoundary(ptr[3]);
    private static bool IsOn(char* ptr)
        => (*ptr | 0x20) == 'o' && (ptr[1] | 0x20) == 'n' && IsBoundary(ptr[2]);
    private static readonly string[] SQLSections = [
        "with",
        "delete from",
        "delete",
        "insert into",
        "insert",
        "values",
        "update",
        "set",
        "select",
        "from",
        "join",
        "inner join",
        "left join",
        "left outer join",
        "right join",
        "right outer join",
        "full join",
        "full outer join",
        "cross join",
        "where",
        "group by",
        "having",
        "union",
        "union all",
        "intersect",
        "except",
        "order by",
        "limit",
        "offset",
        "when",
        "else",
        "then",
        ";"
    ];
    private bool MatchSection(char* ptr, out int secLen) {
        var remaining = (int)(LastChar - ptr);
        for (int i = 0; i < SQLSections.Length; i++) {
            var sec = SQLSections[i];
            secLen = sec.Length;
            if (secLen > remaining || !IsBoundary(ptr[secLen]))
                continue;
            for (int j = 0; j < secLen; j++)
                if (sec[j] != (ptr[j] | 0x20))
                    goto Continue;
            return true;
        Continue:
            continue;
        }
        secLen = 0;
        return false;
    }
    private bool UpdateConditionsEnd(int segmentEndIndex, bool isSection, uint currentExcess, bool keepUnstartedColumn = false) {
        int j = Conditions.Length - 1;
        var nbSectionComment = (int)(LastCondSectionLength >> 16);
        j -= nbSectionComment;
        bool oneMatch = false;
        for (; j >= LastUnfinishedSection; j--) {
            ref var cond = ref Conditions[j];
            if (cond.IsFinished)
                continue;
            if (cond.ParMapOrExcesses != ParMap || (cond.NeedSectionToFinish && !isSection))
                break;
            if (keepUnstartedColumn && cond.Cond is null)
                break;
            if (cond.Cond is null)
                cond.UpdateSelectCond(FindSelectName(isSection ? segmentEndIndex : segmentEndIndex - 1), *CurrentStart, *CurrentExcess);
            cond.Finish(segmentEndIndex, isSection);
            oneMatch = true;
        }
        if (j >= LastUnfinishedSection)
            return oneMatch;
        LastUnfinishedSection = Conditions.Length - nbSectionComment;
        LastCondSectionLength = 0;
        return oneMatch;
    }

    private readonly string FindSelectName(int end) {
        end--;
        while (char.IsWhiteSpace(Builder[end]))
            end--;
        var last = Builder[end];
        var quote = 0;
        if (last == ']')
            quote = '[';
        else if (last == '\'' || last == '`' || last == '"')
            quote = last;
        var start = end;
        end++;
        if (quote != 0) {
            end--;
            start--;
            while (Builder[start] != quote)
                start--;
        }
        else {
            while (!IsBoundary(Builder[start]) && Builder[start] != '.')
                start--;
        }
        start++;
        return new string(Builder, start, end - start);
    }
}
