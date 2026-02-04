using System.Buffers;
using RinkuLib.Tools;

namespace RinkuLib.Queries;

[Flags]
public enum CondFlags : byte {
    None = 0,
    //IsHandlerFollowedByClosingParentesisOrSection = 0b_0000_0100,
    NeedSectionToFinish = 0b_0000_1000,
    IsRequired = 0b_0001_0000,
    //CurrentIsSection = 0b_0010_0000,
    NextIsSection = 0b_0100_0000,
    Finished = 0b_1000_0000,
}
/// <summary>
/// Represents the footprint of a condition marker within the query.
/// Maps a condition key to the specific segment of the parsed string it influences.
/// </summary>
/// <remarks>
/// <para>This structure captures the 'where and what' for the next building phase. 
/// It tracks the range of text associated with a condition, allowing the final builder 
/// to decide which segments to include in the resulting SQL.</para>
/// <list type="bullet">
/// <item>
/// <description><b>Condition Markers:</b> Can be variables (@Var or :Var) or comments (/*Marker*/). 
/// Comments provide more flexibility for complex boolean logic (e.g., /*A&amp;B*/).</description>
/// </item>
/// <item>
/// <description><b>Required Variables:</b> If <see cref="Flags"/> is <see cref="CondFlags.IsRequired"/>, the marker represents 
/// a mandatory parameter (variable) rather than a conditional segment.</description>
/// </item>
/// <item>
/// <description><b>Footprint Mapping:</b> The range from <see cref="StartIndex"/> to <see cref="EndIndex"/> 
/// defines the text segment associated with the condition key.</description>
/// </item>
/// </list>
/// </remarks>
public struct CondInfo {
    public const char AndComment = (char)1;
    public const char AndCommentChar = '&';
    public const char OrComment = (char)2;
    public const char OrCommentChar = '|';
    public const char None = (char)3;
    public const char Select = (char)4;
    public const char JoinedSelect = (char)5;
    public const char SecondarySelect = (char)6;
    public const char SecondaryJoinedSelect = (char)7;
    public const char Special = (char)8;
    public static bool IsJoinedSelect(char type) => type == JoinedSelect || type == SecondaryJoinedSelect;
    public static bool IsNotJoinedSelect(char type) => type == Select || type == SecondarySelect;
    public static bool IsMainSelect(char type) => type == Select || type == JoinedSelect;
    public static bool IsSelect(char type) => type >= Select && type <= SecondaryJoinedSelect;
    public static bool IsComment(char type) => type <= OrComment;
    public static bool IsOther(char type) => IsComment(type) || type == SecondarySelect || type == SecondaryJoinedSelect;
    /// <summary>The identifier for the condition, the key (e.g., a variable name or a name in comment).</summary>
    public string Cond { get; private set; }
    /// <summary>
    /// The start of the string segment influenced by this condition.
    /// </summary>
    public int StartIndex { get; private set; }
    /// <summary>The index where the variable or marker token itself begins in the query string.</summary>
    public int VarIndex { get; private set; }
    /// <summary>
    /// The category of the condition (e.g., None, Comment-based, Select-based, Special...).
    /// Used to determine how the segment should be processed in the next phase.
    /// </summary>
    public char Type { get; private set; }
    //public ulong ParMap { get; private set; }
    /// <summary>
    /// A bitmask tracking nesting depth (Parens/CASE) and SQL section context.
    /// Or excesses of current and previous segment
    /// </summary>
    public ulong ParMapOrExcesses { get; private set; }
    public CondFlags Flags { get; set; }
    /// <summary>The ending index of the SQL segment controlled by this condition.</summary>
    /// <remarks>
    /// May carry the previous segment excess before being finished
    /// </remarks>
    public int EndIndex { get; private set; }
    public static CondInfo NewRequired(string Cond, char Type, int VarIndex)
        => new() {
            Cond = Cond,
            Type = Type,
            VarIndex = VarIndex,
            Flags = CondFlags.IsRequired | CondFlags.Finished
        };
    public static CondInfo NewOptional(string Cond, char Type, int VarIndex, int StartIndex, ulong ParMap, int Excess)
        => new() {
            Cond = Cond,
            Type = Type,
            VarIndex = VarIndex,
            StartIndex = StartIndex,
            ParMapOrExcesses = ParMap,
            EndIndex = Excess
        };
    public static CondInfo NewSelect(int StartIndex, bool isMainSelect, bool isJoined, ulong parMap, int prevExcessExcess) {
        var type = CondInfo.Select;
        if (isJoined)
            type = CondInfo.JoinedSelect;
        if (!isMainSelect)
            type += (char)2;
        return new() {
               Cond = null!,
               Type = type,
               VarIndex = -1,
               StartIndex = StartIndex,
               ParMapOrExcesses = parMap,
               EndIndex = prevExcessExcess
        };
    }
    /// <summary>
    /// Closes the condition's footprint and updates section-related flags in the ParMap.
    /// </summary>
    public void Finish(int endIndex, bool nextIsSection/*, uint excess*/) {
        ParMapOrExcesses = /*(((ulong)excess) << 32UL) | */(ulong)EndIndex;
        Flags |= CondFlags.Finished;
        if (nextIsSection)
            Flags |= CondFlags.NextIsSection;
        /*if (NeedSectionToFinish)
            Flags |= CondFlags.CurrentIsSection;*/
        EndIndex = endIndex;
    }
    public override readonly string ToString()
        => $"{Cond}, {(Type < (char)32 ? (int)Type : Type)}, {StartIndex}, {EndIndex}";
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
    public readonly bool IsFinished => Flags.HasFlag(CondFlags.Finished);
    public readonly bool IsRequired => Flags.HasFlag(CondFlags.IsRequired);
    public readonly bool NeedSectionToFinish => Flags.HasFlag(CondFlags.NeedSectionToFinish);
    public readonly bool NextSegmentIsSection => Flags.HasFlag(CondFlags.NextIsSection);
    //public readonly bool CurrentSegmentIsSection => Flags.HasFlag(CondFlags.CurrentIsSection);
    //public readonly int CurrentSegmentExcess => (int)(uint)ParMapOrExcesses;
    public readonly int PrevSegmentExcess => (int)(uint)ParMapOrExcesses;//(int)(uint)(ParMapOrExcesses >> 32);
}
/// <summary>
/// A pointer-based scanner that identifies condition markers and maps their footprints while normalizing the query string.
/// </summary>
/// <remarks>
/// <para>1. <b>Condition Mapping:</b> Identifies variables (using the specified <c>variableChar</c> like '@' or ':') 
/// and /*Comments*/ as synonymous condition markers. Comments allow for more flexibility.</para>
/// <para>2. <b>Nesting &amp; Scope:</b> Uses a 62-bit stack (<c>ParMap</c>) to track depth. '(' and 'CASE' increment depth, 
/// while ')' and 'END' decrement it, ensuring markers are closed at the correct logical boundary.</para>
/// <para>3. <b>Select Extraction:</b> When enabled, the parser treats individual columns in the SELECT clause 
/// as conditional markers, allowing specific columns to be toggled or identified in the next phase.</para>
/// </remarks>
public unsafe ref struct QueryExtracter {
    /// <summary>Identifier for optional conditions (default '?'). e.g., ?@VarName.</summary>
    public const char OptionalVariableIdentifier = '?';
    /// <summary>Identifier for joining two (or more) footprint together (default '&amp;'). e.g., (SELECT A&amp;, B) or (WHERE A &gt; @A &amp;AND B &lt; @B)</summary>
    public const char JoinAndOrChar = '&';
    public const char CommentAsCommentChar = '~';
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
    public readonly string Binary => ParMap.ConvertBinary();
    private bool PrevBoundary;
    private bool FirstSelectCompleted;
    private bool ContainingParantesis;
    private ulong SelectExtractionParMap;
    private PooledArray<CondInfo> Conditions;
    private uint LastCondSectionLength;
    /// <summary>
    /// Performs a single-pass scan of the query to identify and map condition footprints.
    /// </summary>
    /// <param name="query">The raw SQL input to be parsed.</param>
    /// <param name="extractSelects">If true, individual columns in the SELECT section are captured as <see cref="CondInfo"/> entries.</param>
    /// <param name="variableChar">The character prefix used to identify variables (e.g., '@' or ':').</param>
    /// <param name="newQuery">The resulting normalized SQL string, cleaned of condition markers and formatted for usage.</param>
    /// <returns>
    /// A <see cref="PooledArray{CondInfo}.Locked"/> collection containing the footprints and other metadata for every condition and variable 
    /// found.
    /// </returns>
    /// <exception cref="Exception">Thrown on invalid syntax, such as unclosed comments, quotes, or excessive nesting depth.</exception>
    public static PooledArray<CondInfo>.Locked Segment(string query, char variableChar, out string newQuery) {
        var seg = new QueryExtracter();
        return seg.SegmentQuery(query, variableChar, out newQuery);
    }
    /// <summary>
    /// The primary scanning loop. It uses a single pass with raw pointers to minimize allocations.
    /// The 'Builder' serves as a normalization buffer, while 'Conditions' tracks the metadata
    /// for segments that can be toggled later.
    /// </summary>
    private unsafe PooledArray<CondInfo>.Locked SegmentQuery(string query, char variableChar, out string newQuery) {
        Length = query.Length;
        if (Length <= 1)
            throw new Exception($"invalid query \"{query}\", must contains at least 2 letters");
        Conditions = new PooledArray<CondInfo>();
        Builder = ArrayPool<char>.Shared.Rent((int)(Length * 1.1));
        BuilderSpan = Builder;
        BuilderInd = 0;
        CurrentQuote = 0;
        PrevBoundary = true;
        var startIndexes = ArrayPool<int>.Shared.Rent(64);
        var excesses = ArrayPool<int>.Shared.Rent(64);
        ParMap = 1;

        fixed (int* ps = startIndexes)
        fixed (int* pe = excesses)
        fixed (char* p = query) {
            CurrentChar = p;
            CurrentStart = ps;
            CurrentExcess = pe;
            LastChar = p + Length;
            for (; CurrentChar < LastChar; CurrentChar++) {
                Builder[BuilderInd++] = *CurrentChar;
                if (IsBoundary(*CurrentChar)) {
                    ManageBoundary();
                    continue;
                }
                if (*CurrentChar == OptionalVariableIdentifier && CurrentChar[1] != variableChar) {
                    if (CurrentChar[1] == OptionalVariableIdentifier
                        && CurrentChar[2] == OptionalVariableIdentifier) {
                        BuilderInd--;
                        UpdateConditionsEnd(BuilderInd, false, 0);
                        UpdateCurrentStart(BuilderInd, 0);
                        CurrentChar += 2;
                    }
                    continue;
                }
                if (!PrevBoundary || CurrentQuote != 0)
                    continue;
                PrevBoundary = false;
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
                else if (IsCase(CurrentChar)) {
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

    private void ManageJoinedSelect() {
        if (SelectExtractionParMap != ParMap)
            return;
        int i = Conditions.Length - 1;
        for (; i >= 0; i--)
            if (CondInfo.IsNotJoinedSelect(Conditions[i].Type))
                break;
        if (i < 0)
            throw new Exception("the first join has nothing to join to");
        ref var targetCond = ref Conditions[i];
        if (targetCond.Cond is null)
            targetCond.UpdateSelectCond(FindSelectName(BuilderInd - 1), *CurrentStart, *CurrentExcess);
        Conditions.Add(CondInfo.NewSelect(targetCond.StartIndex, !FirstSelectCompleted, true, ParMap, 1));
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
        if ((secLen == 6 || secLen == 11) && (IsInsert(CurrentChar) || IsValues(CurrentChar)))
            ContainingParantesis = true;
        else if (ContainingParantesis && ParMap == 1)
            ContainingParantesis = false;
        if (ParMap == SelectExtractionParMap) {
            SelectExtractionParMap = 0;
            FirstSelectCompleted = true;
        }
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
            Conditions.Add(CondInfo.NewSelect(-1, !FirstSelectCompleted, false, ParMap, 0));
        }
        return true;
    }

    private const ulong BoundaryMask = 0x930100002601;
    private static unsafe bool IsBoundary(char c)
        => c < 64 && (BoundaryMask >> c & 1) == 1;
    private void ManageBoundary() {
        var c = *CurrentChar;
        if (ManageQuote(c)) {
            PrevBoundary = true;
            return;
        }
        if (CurrentQuote != 0)
            return;
        if (TryManageComment(true)) {
            CurrentChar--;
            return;
        }
        PrevBoundary = true;
        if (c == '(')
            RaiseParentesis(true);
        else if (c == ')')
            LowerParentesis();
        else if (c == ',')
            ManageComa();
    }

    private void ManageComa() {
        if (CurrentChar[-1] == JoinAndOrChar) {
            BuilderInd--;
            Builder[BuilderInd - 1] = ',';
            ManageJoinedSelect();
            return;
        }
        UpdateConditionsEnd(BuilderInd, false, 1);
        UpdateCurrentStart(BuilderInd, 1);
        if (SelectExtractionParMap == ParMap)
            Conditions.Add(CondInfo.NewSelect(BuilderInd, !FirstSelectCompleted, false, ParMap, 1));
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
        while (!IsBoundary(*CurrentChar) && *CurrentChar != JoinAndOrChar) {
            Builder[BuilderInd++] = *CurrentChar;
            CurrentChar++;
        }
        var type = CondInfo.None;
        var varLength = BuilderInd - varIndex;
        if (*(CurrentChar - 2) == '_') {
            type = *(CurrentChar - 1);
            varLength -= 2;
        }
        var cond = new string(Builder, varIndex, varLength);
        if (isRequired)
            Conditions.Add(CondInfo.NewRequired(cond, type, varIndex));
        else {
            var decal = GetDecalToSectionLevel(ParMap);
            Conditions.Add(CondInfo.NewOptional(cond, type, varIndex, CurrentStart[-decal], ParMap >> decal, CurrentExcess[-decal]));
        }
        if (type >= CondInfo.Special) {
            var c = CurrentChar;
            while (char.IsWhiteSpace(*c))
                c++;
            /*if (*c == ')' || MatchSection(c, out _))
                Conditions.Last.Flags |= CondFlags.IsHandlerFollowedByClosingParentesisOrSection;*/
        }
        CurrentChar--;
        return true;
    }
    private bool TryManageComment(bool currentCharAddedToBuilder) {
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
                throw new Exception("comment unclosed");
            CurrentChar++;
            Builder[BuilderInd++] = '*';
            Builder[BuilderInd++] = '/';
            return false;
        }
        var type = CondInfo.AndComment;
        var nbCond = 0;
        while (true) {
            var cond = GetCommentString();
            if (string.IsNullOrWhiteSpace(cond))
                continue;
            nbCond++;
            Conditions.Add(CondInfo.NewOptional(cond, type, BuilderInd - 1, *CurrentStart, ParMap, *CurrentExcess));
            if ((*CurrentChar == '*' && CurrentChar[1] == '/') || CurrentChar >= LastChar)
                break;
            type = *CurrentChar == CondInfo.OrCommentChar ? CondInfo.OrComment : CondInfo.AndComment;
            CurrentChar++;
        }
        CurrentChar += 2;
        SkipWhiteSpace();
        if (nbCond > 0 && MatchSection(CurrentChar, out var secLen)) {
            LastCondSectionLength = (uint)nbCond << 16 | (uint)secLen;
            for (; nbCond > 0; nbCond--)
                Conditions[Conditions.Length - nbCond].UpdateCommentAsSectionComment(BuilderInd - 1);
        }
        return true;
    }
    private void SkipWhiteSpace() {
        while (char.IsWhiteSpace(*CurrentChar)) {
            Builder[BuilderInd++] = *CurrentChar;
            CurrentChar++;
        }
    }
    private string GetCommentString() {
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
            throw new Exception("comment unclosed");
        var i = (int)(CurrentChar - start);
        while (char.IsWhiteSpace(start[i]))
            i--;
        return new string(start, 0, i);
    }
    private bool ManageQuote(char c) {
        if (c == CurrentQuote) {
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
    private void RaiseParentesis(bool checkSection) {
        if (ParMap >= 0x8000000000000000UL)
            throw new Exception("cannot have more than 64 level deep of parentesis / cases");
        CurrentStart++;
        CurrentExcess++;
        UpdateCurrentStart(BuilderInd, 0);
        ParMap <<= 1;
        if (!checkSection)
            return;
        CurrentChar++;
        SkipWhiteSpace();
        if (TryManageComment(false))
            if (LastCondSectionLength > 0)
                ParMap |= 1;
        if (MatchSection(CurrentChar, out _) || (ParMap == 0b10 && ContainingParantesis))
            ParMap |= 1;
        CurrentChar--;
    }
    private void LowerParentesis() {
        UpdateConditionsEnd(BuilderInd - 1, true, 0);
        if (ParMap == 1)
            throw new Exception("too many closing parentesis / cases");
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
    private static unsafe bool IsInsert(char* ptr)
        => (*ptr | 0x20) == 'i' && (ptr[1] | 0x20) == 'n' && (ptr[2] | 0x20) == 's'
        && (ptr[3] | 0x20) == 'e' && (ptr[4] | 0x20) == 'r' && (ptr[5] | 0x20) == 't';
    private static unsafe bool IsSelect(char* ptr)
        => (*ptr | 0x20) == 's' && (ptr[1] | 0x20) == 'e' && (ptr[2] | 0x20) == 'l'
        && (ptr[3] | 0x20) == 'e' && (ptr[4] | 0x20) == 'c' && (ptr[5] | 0x20) == 't';
    private static unsafe bool IsValues(char* ptr)
        => (*ptr | 0x20) == 'v' && (ptr[1] | 0x20) == 'a' && (ptr[2] | 0x20) == 'l'
        && (ptr[3] | 0x20) == 'u' && (ptr[4] | 0x20) == 'e' && (ptr[5] | 0x20) == 's';
    private static unsafe bool IsCase(char* ptr)
        => (*ptr | 0x20) == 'c' && (ptr[1] | 0x20) == 'a' && (ptr[2] | 0x20) == 's' && (ptr[3] | 0x20) == 'e';
    private static bool IsEnd(char* ptr)
        => (*ptr | 0x20) == 'e' && (ptr[1] | 0x20) == 'n' && (ptr[2] | 0x20) == 'd' && IsBoundary(ptr[3]);
    public static unsafe bool IsOr(char* ptr)
        => (*ptr | 0x20) == 'o' && (ptr[1] | 0x20) == 'r' && IsBoundary(ptr[2]);
    public static unsafe bool IsAnd(char* ptr)
        => (*ptr | 0x20) == 'a' && (ptr[1] | 0x20) == 'n' && (ptr[2] | 0x20) == 'd' && IsBoundary(ptr[3]);
    public static unsafe bool IsOn(char* ptr)
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
    private static bool MatchSection(char* ptr, out int secLen) {
        for (int i = 0; i < SQLSections.Length; i++) {
            var sec = SQLSections[i];
            secLen = sec.Length;
            if (!IsBoundary(ptr[secLen]))
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
    private bool UpdateConditionsEnd(int segmentEndIndex, bool isSection, uint currentExcess) {
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
            if (CondInfo.IsSelect(cond.Type) && cond.Cond is null)
                cond.UpdateSelectCond(FindSelectName(isSection ? segmentEndIndex : segmentEndIndex - 1), *CurrentStart, *CurrentExcess);
            cond.Finish(segmentEndIndex, isSection/*, currentExcess*/);
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
            while (Builder[start] == quote)
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