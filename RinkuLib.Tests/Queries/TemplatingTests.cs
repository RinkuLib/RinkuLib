using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.Queries;

public record struct SegmentVerify(string ExpectedContent, int ExpectedTrim, bool IsSection);
public record struct ConditionVerify(string ExpectedKey, string ConditionSegment, int NbSameCondContext, bool IsOr = false);

public class TemplatingTests {
    private static void Verify(QueryFactory factory, SegmentVerify[] expectedSegments, ConditionVerify[] expectedConditions, string[] keys) {
        var actualSegments = factory.Segments;
        Assert.Equal(expectedSegments.Length, actualSegments.Length);
        var mapper = factory.Mapper;
        Assert.Equal(keys.Length, mapper.Count);
        for (int i = 0; i < keys.Length; i++)
            Assert.Equal(keys[i], mapper.GetKey(i));
        var query = factory.Query;
        for (int i = 0; i < expectedSegments.Length; i++) {
            var actual = actualSegments[i];
            var expected = expectedSegments[i];
            var actualContent = query.Substring(actual.Start, actual.Length);

            Assert.Equal(expected.ExpectedContent, actualContent);
            Assert.Equal(expected.IsSection, actual.IsSection);

            if (actual.Handler is null) {
                Assert.Equal(expected.ExpectedTrim, actual.ExcessOrInd);
                continue;
            }
            var key = mapper.GetKey(actual.ExcessOrInd);
            Assert.True(actualContent.AsSpan(0, actualContent.Length - 2).Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        var actualConditions = factory.Conditions;
        Assert.Equal(expectedConditions.Length, actualConditions.Length - 1);
        Assert.Equal(mapper.Count, actualConditions[expectedConditions.Length].CondIndex);
        int contextMasterCondIndex = -1;
        bool checkingOr = false;
        for (int i = 0; i < expectedConditions.Length; i++) {
            var actual = actualConditions[i];
            var expected = expectedConditions[i];
            if (contextMasterCondIndex == -1)
                contextMasterCondIndex = i;

            Assert.Equal(expected.ExpectedKey, mapper.GetKey(actual.CondIndex));
            var startSegment = actualSegments[actual.SegmentInd];
            var lastSegment = actualSegments[actual.SegmentInd + actual.Length - 1];
            if (expected.IsOr) {
                checkingOr = true;
                Assert.Equal(expected.NbSameCondContext, -actual.NbConditionSkip);
            }
            else {
                if (expectedConditions[contextMasterCondIndex].NbSameCondContext == expected.NbSameCondContext)
                    Assert.Equal(expected.NbSameCondContext, actual.NbConditionSkip + (i - contextMasterCondIndex));
                else
                    Assert.Equal(expected.NbSameCondContext, actual.NbConditionSkip);
                if (checkingOr) {
                    lastSegment = actualSegments[actual.SegmentInd + actualConditions[contextMasterCondIndex].Length - 1];
                    Assert.Equal(expectedConditions[contextMasterCondIndex].NbSameCondContext, actual.Length + (i - contextMasterCondIndex) + 1);
                }
            }
            if (actual.NbConditionSkip == 1) {
                checkingOr = false;
                contextMasterCondIndex = -1;
            }
            var endIndex = lastSegment.Start + lastSegment.Length;
            Assert.Equal(expected.ConditionSegment, query[startSegment.Start..endIndex]);
        }
    }
    [Fact]
    public void With_complete_condition() {
        var sql = "WITH/*cte*/ parentTable AS (SELECT column1, column2 FROM table_name WHERE cond = 1) SELECT ID, Username, Email FROM Users WHERE IsActive = @Active";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        // No markers mean the entire query is one "Always" segment.
        var expectedSegments = new[] {
            new SegmentVerify("WITH", 4, false),
            new SegmentVerify(" parentTable AS (SELECT column1, column2 FROM table_name WHERE cond = 1)", 0, false),
            new SegmentVerify(" SELECT ID, Username, Email FROM Users WHERE IsActive = @Active", 0, true)
        };

        var expectedConditions = new[] {
            new ConditionVerify("cte", " parentTable AS (SELECT column1, column2 FROM table_name WHERE cond = 1)", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["cte", "@Active"]);
    }
    [Fact]
    public void Ignored_Comment() {
        var sql = "SELECT /*~ optimizer hint */ ID, Username, Email FROM Users WHERE IsActive = @Active";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        // No markers mean the entire query is one "Always" segment.
        var expectedSegments = new[] {
            new SegmentVerify("SELECT /* optimizer hint */ ID, Username, Email FROM Users WHERE IsActive = @Active", 0, false)
        };

        // No conditional keys are extracted.
        var expectedConditions = Array.Empty<ConditionVerify>();

        Verify(factory, expectedSegments, expectedConditions, ["@Active"]);
    }
    [Fact]
    public void Ignored_Comment_InOptional() {
        var sql = "SELECT /*~ optimizer hint *//*ID*/ ID, Username, Email FROM Users WHERE IsActive = @Active";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        // No markers mean the entire query is one "Always" segment.
        var expectedSegments = new[] {
            new SegmentVerify("SELECT", 6, false),
            new SegmentVerify(" /* optimizer hint */ ID,", 0, false),
            new SegmentVerify(" Username, Email FROM Users WHERE IsActive = @Active", 0, false)
        };


        var expectedConditions = new[] {
            new ConditionVerify("ID", " /* optimizer hint */ ID,", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["ID", "@Active"]);
    }
    [Fact]
    public void Example1_StaticQuery() {
        var sql = "SELECT ID, Username, Email FROM Users WHERE IsActive = @Active";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        // No markers mean the entire query is one "Always" segment.
        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Username, Email FROM Users WHERE IsActive = @Active", 0, false)
        };

        // No conditional keys are extracted.
        var expectedConditions = Array.Empty<ConditionVerify>();

        Verify(factory, expectedSegments, expectedConditions, ["@Active"]);
    }
    [Fact]
    public void Forced_Boundary() {
        var sql = "SELECT TOP (1)??? /*ID*/ID, Username, Email FROM Users WHERE IsActive = @Active";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        // No markers mean the entire query is one "Always" segment.
        var expectedSegments = new[] {
            new SegmentVerify("SELECT TOP (1)", 0, false),
            new SegmentVerify(" ID,", 0, false),
            new SegmentVerify(" Username, Email FROM Users WHERE IsActive = @Active", 0, false)
        };


        var expectedConditions = new[] {
            new ConditionVerify("ID", " ID,", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["ID", "@Active"]);
    }
    [Fact]
    public void Using_Empty_Join_Conds() {
        var sql = "SELECT ID, Username FROM Users INNER JOIN Table t ON t.ID = ?@Cond WHERE IsActive = 1 AND Status = ?@Status";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Username FROM Users INNER JOIN Table t ON", 0, false),
            new SegmentVerify(" t.ID = @Cond", 0, false),
            new SegmentVerify(" WHERE IsActive = 1 AND", 4, true),
            new SegmentVerify(" Status = @Status", 0, false)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@Cond", " t.ID = @Cond", 1),
            new ConditionVerify("@Status", " Status = @Status", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Cond", "@Status"]);
    }
    [Fact]
    public void Example2_OptionalVariableFilter() {
        var sql = "SELECT ID, Username FROM Users WHERE IsActive = 1 AND Status = ?@Status";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Username FROM Users WHERE IsActive = 1 AND", 4, false),
            new SegmentVerify(" Status = @Status", 0, false)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@Status", " Status = @Status", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Status"]);
    }
    [Fact]
    public void Example3_BooleanToggle_KeywordCleanup() {
        var sql = "SELECT ID, Username, Email FROM Users WHERE /*ActiveOnly*/Active = 1 ORDER BY Username";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Username, Email FROM Users WHERE", 6, false),
            new SegmentVerify(" Active = 1", 0, false),
            new SegmentVerify(" ORDER BY Username", 0, true)
        };

        var expectedConditions = new[] {
            new ConditionVerify("ActiveOnly", " Active = 1", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["ActiveOnly"]);
    }

    [Fact]
    public void Example4_FunctionalFootprint() {
        var sql = "SELECT ID, u.Name FROM Users u WHERE u.Name LIKE CONCAT('%', ?@Name, '%')";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, u.Name FROM Users u WHERE", 6, false),
            new SegmentVerify(" u.Name LIKE CONCAT('%', @Name, '%')", 0, false)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@Name", " u.Name LIKE CONCAT('%', @Name, '%')", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Name"]);
    }

    [Fact]
    public void Example5_ImplicitAnd_SharedFootprint() {
        var sql = "SELECT ID, Name FROM Products WHERE Price * ?@Modifier > ?@Minimum";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Name FROM Products WHERE", 6, false),
            new SegmentVerify(" Price * @Modifier > @Minimum", 0, false)
        };

        // Shared footprint means one condition segment, but two keys mapped to it
        var expectedConditions = new[] {
            new ConditionVerify("@Modifier", " Price * @Modifier > @Minimum", 2),
            new ConditionVerify("@Minimum", " Price * @Modifier > @Minimum", 2)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Modifier", "@Minimum"]);
    }

    [Fact]
    public void Example6_ContextJoining() {
        var sql = "SELECT * FROM Products WHERE Price IS NOT NULL &AND Price > ?@MinPrice";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
        new SegmentVerify("SELECT * FROM Products WHERE", 6, false),
        new SegmentVerify(" Price IS NOT NULL AND Price > @MinPrice", 0, false)
    };

        var expectedConditions = new[] {
        new ConditionVerify("@MinPrice", " Price IS NOT NULL AND Price > @MinPrice", 1)
    };

        Verify(factory, expectedSegments, expectedConditions, ["@MinPrice"]);
    }

    [Fact]
    public void Example7_SectionToggle_JoinDependency() {
        var sql = "SELECT p.ID, p.Name FROM Products p /*@VendorName*/INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = ?@VendorName";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT p.ID, p.Name FROM Products p", 0, false),
            new SegmentVerify(" INNER JOIN Vendors v ON v.ID = p.VendorID", 0, false),
            new SegmentVerify(" WHERE p.IsActive = 1 AND", 4, true),
            new SegmentVerify(" v.VendorName = @VendorName", 0, false)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@VendorName", " INNER JOIN Vendors v ON v.ID = p.VendorID", 1),
            new ConditionVerify("@VendorName", " v.VendorName = @VendorName", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@VendorName"]);
    }

    [Fact]
    public void Example8_LinearLogic_AndGate() {
        var sql = "SELECT ID, Username, Email, /*Internal&Authorized*/SocialSecurityNumber FROM Users";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Username, Email,", 1, false),
            new SegmentVerify(" SocialSecurityNumber", 0, false),
            new SegmentVerify(" FROM Users", 0, true)
        };

        var expectedConditions = new[] {
            new ConditionVerify("Internal", " SocialSecurityNumber", 2),
            new ConditionVerify("Authorized", " SocialSecurityNumber", 2)
        };

        Verify(factory, expectedSegments, expectedConditions, ["Internal", "Authorized"]);
    }

    [Fact]
    public void Example9_AtomicSubquery_FootprintExtension() {
        var sql = "SELECT ID, Name FROM Users WHERE /*@ActionType*/(SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Name FROM Users WHERE", 6, false),
            new SegmentVerify(" (SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0", 0, false)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@ActionType", " (SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@ActionType"]);
    }
    [Fact]
    public void Example10_CollectionHandler_X() {
        var sql = "SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT * FROM Tasks WHERE", 6, false),
            new SegmentVerify(" CategoryID IN (", 0, false),
            new SegmentVerify("@Cats_X", 0, false),
            new SegmentVerify(")", 0, false)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@Cats", " CategoryID IN (@Cats_X)", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Cats"]);
    }

    [Fact]
    public void Example11_PassengerDependency_Enclosure() {
        // Both variables share a segment because FETCH is not a keyword anchor
        var sql = "SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT Name FROM Products ORDER BY ID OFFSET", 7, false),
            new SegmentVerify(" ", 0, false),
            new SegmentVerify("@Skip_N", 0, false),
            new SegmentVerify(" ROWS FETCH NEXT ", 0, false),
            new SegmentVerify("@Take_N", 0, false),
            new SegmentVerify(" ROWS ONLY", 0, false)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@Skip", " @Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Skip", "@Take"]);
    }

    [Fact]
    public void Example12_RawInjectionHandler_R() {
        var sql = "SELECT ID, Name FROM @Table_R WHERE IsActive = 1 AND Name = @Name_S";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Name FROM ", 0, false),
            new SegmentVerify("@Table_R", 0, false),
            new SegmentVerify(" WHERE IsActive = 1 AND Name = ", 0, false),
            new SegmentVerify("@Name_S", 0, false)
        };

        Verify(factory, expectedSegments, [], ["@Table", "@Name"]);
    }

    [Fact]
    public void Example13_DynamicProjectionAndGrouping() {
        var sql = "SELECT /*Agg*/COUNT(*) AS Total&, SUM(Price) AS Revenue, p.CategoryName, /*NotAgg*/p.BrandName&, p.ID FROM Products p WHERE p.IsActive = 1 /*Agg*/GROUP BY p.CategoryName, p.BrandName";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT", 6, false),
            new SegmentVerify(" COUNT(*) AS Total, SUM(Price) AS Revenue,", 0, false),
            new SegmentVerify(" p.CategoryName,", 1, false),
            new SegmentVerify(" p.BrandName, p.ID", 0, false),
            new SegmentVerify(" FROM Products p WHERE p.IsActive = 1", 0, true),
            new SegmentVerify(" GROUP BY p.CategoryName, p.BrandName", 0, false) 
        };

        var expectedConditions = new[] {
            new ConditionVerify("Agg", " COUNT(*) AS Total, SUM(Price) AS Revenue,", 1),
            new ConditionVerify("NotAgg", " p.BrandName, p.ID", 1),
            new ConditionVerify("Agg", " GROUP BY p.CategoryName, p.BrandName", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["Agg", "NotAgg"]);
    }

    [Fact]
    public void Example14_ColumnJoining_AmperComma() {
        var sql = "SELECT ID, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM Users";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT ID, Username,", 1, false),
            new SegmentVerify(" City, Street, ZipCode", 0, false),
            new SegmentVerify(" FROM Users", 0, true)
        };

        Verify(factory, expectedSegments, [new ConditionVerify("IncludeAddress", " City, Street, ZipCode", 1)], ["IncludeAddress"]);
    }

    [Fact]
    public void Example15_UpdateListCleanup() {
        var sql = "UPDATE Users SET LastModified = GETDATE(), Username = ?@Username, Email = ?@Email WHERE ID = @ID";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("UPDATE Users SET LastModified = GETDATE(),", 1, false),
            new SegmentVerify(" Username = @Username,", 1, false),
            new SegmentVerify(" Email = @Email", 0, false),
            new SegmentVerify(" WHERE ID = @ID", 0, true)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@Username", " Username = @Username,", 1),
            new ConditionVerify("@Email", " Email = @Email", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Username", "@Email", "@ID"]);
    }

    [Fact]
    public void Example16_InsertColumnDependency() {
        var sql = "INSERT INTO Users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("INSERT INTO Users (Username,", 1, false),
            new SegmentVerify(" Email", 0, false),
            new SegmentVerify(") VALUES (@Username,", 1, true),
            new SegmentVerify(" @Email", 0, false),
            new SegmentVerify(")", 0, true)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@Email", " Email", 1),
            new ConditionVerify("@Email", " @Email", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Username", "@Email"]);
    }

    [Fact]
    public void Example17_MultiColumnInsert() {
        var sql = "INSERT INTO Profiles (UserID, /*Details*/Bio&, Website&, AvatarURL) VALUES (@UID, /*Details*/@Bio&, @Web&, @Img)";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("INSERT INTO Profiles (UserID,", 1, false),
            new SegmentVerify(" Bio, Website, AvatarURL", 0, false),
            new SegmentVerify(") VALUES (@UID,", 1, true),
            new SegmentVerify(" @Bio, @Web, @Img", 0, false),
            new SegmentVerify(")", 0, true)
        };

        var expectedConditions = new[] {
            new ConditionVerify("Details", " Bio, Website, AvatarURL", 1),
            new ConditionVerify("Details", " @Bio, @Web, @Img", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["Details", "@UID", "@Bio", "@Web", "@Img"]);
    }

    [Fact]
    public void Example17_MultiColumnInsert_Alternative() {
        var sql = "INSERT INTO Profiles (UserID, /*@Bio&@Web&@Img*/Bio&, Website&, AvatarURL) VALUES (@UID, ?@Bio&, ?@Web&, ?@Img)";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("INSERT INTO Profiles (UserID,", 1, false),
            new SegmentVerify(" Bio, Website, AvatarURL", 0, false),
            new SegmentVerify(") VALUES (@UID,", 1, true),
            new SegmentVerify(" @Bio, @Web, @Img", 0, false),
            new SegmentVerify(")", 0, true)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@Bio", " Bio, Website, AvatarURL", 3),
            new ConditionVerify("@Web", " Bio, Website, AvatarURL", 3),
            new ConditionVerify("@Img", " Bio, Website, AvatarURL", 3),
            new ConditionVerify("@Bio", " @Bio, @Web, @Img", 3),
            new ConditionVerify("@Web", " @Bio, @Web, @Img", 3),
            new ConditionVerify("@Img", " @Bio, @Web, @Img", 3)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@UID", "@Bio", "@Web", "@Img"]);
    }

    [Fact]
    public void Example18_DeleteAndCleanup() {
        var sql = "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND /*PurgeOldOnly*/IsArchived = 1";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND", 4, false),
            new SegmentVerify(" IsArchived = 1", 0, false)
        };

        Verify(factory, expectedSegments, [new ConditionVerify("PurgeOldOnly", " IsArchived = 1", 1)], ["PurgeOldOnly"]);
    }

    [Fact]
    public void Example18_DeleteAndCleanup_Alternative() {
        var sql = "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND (/*PurgeFromAll*/1=1 OR IsArchived = 1)";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND (", 0, false),
            new SegmentVerify("1=1 OR", 0, false),
            new SegmentVerify(" IsArchived = 1)", 0, false)
        };

        Verify(factory, expectedSegments, [new ConditionVerify("PurgeFromAll", "1=1 OR", 1)], ["PurgeFromAll"]);
    }

    [Fact]
    public void Example19_DynamicOrderBy_ClauseCleanup() {
        var sql = "SELECT * FROM Products WHERE IsActive = 1 /*@Sort*/ORDER BY @Sort_R ?@Dir_R";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
        new SegmentVerify("SELECT * FROM Products WHERE IsActive = 1", 0, false),
        new SegmentVerify(" ORDER BY", 9, false),
        new SegmentVerify(" ", 0, false),
        new SegmentVerify("@Sort_R", 0, false),
        new SegmentVerify(" ", 0, false),
        new SegmentVerify("@Dir_R", 0, false)
    };

        var expectedConditions = new[] {
        new ConditionVerify("@Sort", " ORDER BY @Sort_R @Dir_R", 2),
        new ConditionVerify("@Dir", " @Sort_R @Dir_R", 2)
    };

        Verify(factory, expectedSegments, expectedConditions, ["@Sort", "@Dir"]);
    }

    [Fact]
    public void OrTest_SectionToggle_JoinDependency() {
        var sql = "SELECT p.ID, p.Name, /*VendorName*/v.VendorName FROM Products p /*VendorName|@VendorName*/INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = ?@VendorName";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT p.ID, p.Name,", 1, false),
            new SegmentVerify(" v.VendorName", 0, false),
            new SegmentVerify(" FROM Products p", 0, true),
            new SegmentVerify(" INNER JOIN Vendors v ON v.ID = p.VendorID", 0, false),
            new SegmentVerify(" WHERE p.IsActive = 1 AND", 4, true),
            new SegmentVerify(" v.VendorName = @VendorName", 0, false)
        };

        var expectedConditions = new[] {
            new ConditionVerify("VendorName", " v.VendorName", 1),
            new ConditionVerify("VendorName", " INNER JOIN Vendors v ON v.ID = p.VendorID", 2, true),
            new ConditionVerify("@VendorName", " INNER JOIN Vendors v ON v.ID = p.VendorID", 2),
            new ConditionVerify("@VendorName", " v.VendorName = @VendorName", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["VendorName", "@VendorName"]);
    }
    [Fact]
    public void ConditionInCase_Variable() {
        // If the optional @Category matches the Category column, check if Active = 1
        var sql = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);

        var expectedSegments = new[] {
            new SegmentVerify("SELECT * FROM Products WHERE", 6, false),
            new SegmentVerify(" CASE WHEN", 5, false),
            new SegmentVerify(" Category = @Category", 0, false),
            new SegmentVerify(" THEN 1 WHEN Category = 0 THEN", 5, true),
            new SegmentVerify(" ", 0, false),
            new SegmentVerify("@NoCategory_S", 0, false),
            new SegmentVerify(" ELSE 0 END = @CatFlag", 0, true)
        };

        var expectedConditions = new[] {
            new ConditionVerify("@CatFlag", " CASE WHEN Category = @Category THEN 1 WHEN Category = 0 THEN @NoCategory_S ELSE 0 END = @CatFlag", 3),
            new ConditionVerify("@Category", " Category = @Category", 1),
            new ConditionVerify("@NoCategory", " @NoCategory_S", 1)
        };

        Verify(factory, expectedSegments, expectedConditions, ["@Category", "@CatFlag", "@NoCategory"]);
    }
    [Fact]
    public void Extract_Select() {
        var sql = "?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);
        var expectedSegments = new[] {
            new SegmentVerify("SELECT", 6, false),
            new SegmentVerify(" ID,", 1, false),
            new SegmentVerify(" Username,", 1, false),
            new SegmentVerify(" Email, Test", 0, false),
            new SegmentVerify(" FROM Users WHERE IsActive = 1", 0, true),
        };

        var expectedConditions = new[] {
            new ConditionVerify("ID", " ID,", 1),
            new ConditionVerify("Username", " Username,", 1),
            new ConditionVerify("Email", " Email, Test", 2, true),
            new ConditionVerify("Test", " Email, Test", 2),
        };
        Verify(factory, expectedSegments, expectedConditions, ["ID", "Username", "Email", "Test"]);
    }
    [Fact]
    public void Extract_Select_Union() {
        var sql = "?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1 UNION ALL ?SELECT ID, Username, Email&, Test FROM Other";
        var factory = new QueryFactory(sql, '@', SpecialHandler.SpecialHandlerGetter.PresenceMap);
        var expectedSegments = new[] {
            new SegmentVerify("SELECT", 6, false),
            new SegmentVerify(" ID,", 1, false),
            new SegmentVerify(" Username,", 1, false),
            new SegmentVerify(" Email, Test", 0, false),
            new SegmentVerify(" FROM Users WHERE IsActive = 1 UNION ALL SELECT", 7, true),
            new SegmentVerify(" ID,", 1, false),
            new SegmentVerify(" Username,", 1, false),
            new SegmentVerify(" Email, Test", 0, false),
            new SegmentVerify(" FROM Other", 0, true),
        };

        var expectedConditions = new[] {
            new ConditionVerify("ID", " ID,", 1),
            new ConditionVerify("Username", " Username,", 1),
            new ConditionVerify("Email", " Email, Test", 2, true),
            new ConditionVerify("Test", " Email, Test", 2),
            new ConditionVerify("ID", " ID,", 1),
            new ConditionVerify("Username", " Username,", 1),
            new ConditionVerify("Email", " Email, Test", 2, true),
            new ConditionVerify("Test", " Email, Test", 2),
        };
        Verify(factory, expectedSegments, expectedConditions, ["ID", "Username", "Email", "Test"]);
    }
}
