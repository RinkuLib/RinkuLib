using RinkuLib.DbParsing;
using RinkuLib.Queries;
using Xunit;

namespace RinkuLib.Tests.Queries; 
public class QueryParsingTests {
    public static readonly DummyConnection DummyCnn = new();

    private static void Verify(QueryBuilder builder, string finalQuery, ValueTuple<string, object>[] parameters) {
        var cmd = DummyCnn.CreateDummyCommand();
        builder.QueryCommand.SetCommand(cmd, builder.Variables);
        var actualParameters = cmd.ParametersList;
        Assert.Equal(parameters.Length, actualParameters.Count);
        for (int i = 0; i < parameters.Length; i++) {
            Assert.Equal(parameters[i].Item1, actualParameters[i].ParameterName);
            Assert.Equal(parameters[i].Item2, actualParameters[i].Value);
        }
        Assert.Equal(finalQuery, cmd.CommandText);
    }

    [Fact]
    public void With_complete_condition() {
        var sql = "WITH/*cte*/ parentTable AS (SELECT column1, column2 FROM table_name WHERE cond = 1) SELECT ID, Username, Email FROM Users WHERE IsActive = @Active";
        var query = new QueryCommand(sql);
        var builder = query.StartBuilder();
        Verify(builder, " SELECT ID, Username, Email FROM Users WHERE IsActive = @Active", []);
    }
    [Fact]
    public void With_inner_condition() {
        var sql = "WITH parentTable AS (SELECT column1, column2 FROM table_name WHERE cond = ?@inner) SELECT ID, Username, Email FROM Users WHERE IsActive = @Active";
        var query = new QueryCommand(sql);
        var builder = query.StartBuilder();
        Verify(builder, "WITH parentTable AS (SELECT column1, column2 FROM table_name) SELECT ID, Username, Email FROM Users WHERE IsActive = @Active", []);
    }
    [Fact]
    public void Example1_StaticQuery() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT ID, Username, Email FROM Users WHERE IsActive = @Active", []);
    }
    [Fact]
    public void Example1_UnaviableParams() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE IsActive = @Active");
        var builder = query.StartBuilder();
        // @Status is not provided
        Assert.False(builder.Use("NotInQuery", true));
        Assert.False(builder.Use("@NotInQuery", true));
        Assert.False(builder.Use("NotInQuery"));
    }
    [Fact]
    public void Example2_OptionalVariableFilter_NotProvided() {
        var query = new QueryCommand("SELECT ID, Username FROM Users WHERE IsActive = 1 AND Status = ?@Status");
        var builder = query.StartBuilder();
        // @Status is not provided
        Verify(builder, "SELECT ID, Username FROM Users WHERE IsActive = 1", []);
    }
    [Fact]
    public void Conditional_FirstCol_Usage() {
        var query = new QueryCommand("SELECT DISTINCT??? /*ShowId*/ID, Name FROM Users");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT DISTINCT Name FROM Users", []);
    }
    [Fact]
    public void Conditional_FirstCol_Usage_No_Forced() {
        var query = new QueryCommand("SELECT DISTINCT /*ShowId*/ID, Name FROM Users");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT Name FROM Users", []);
    }
    [Fact]
    public void Conditional_Distinct_Usage() {
        var query = new QueryCommand("SELECT /*UseDistinct*/DISTINCT??? ID, Name FROM Users");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT ID, Name FROM Users", []);
    }
    [Fact]
    public void Example2_OptionalVariableFilter_Provided() {
        var query = new QueryCommand("SELECT ID, Username FROM Users WHERE IsActive = 1 AND Status = ?@Status");
        var builder = query.StartBuilder();
        builder.Use("@Status", "Active");
        Verify(builder, "SELECT ID, Username FROM Users WHERE IsActive = 1 AND Status = @Status", [("@Status", "Active")]);
    }
    [Fact]
    public void Example3_BooleanToggle_NotProvided() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE /*ActiveOnly*/Active = 1 ORDER BY Username");
        var builder = query.StartBuilder();
        // ActiveOnly not used
        Verify(builder, "SELECT ID, Username, Email FROM Users ORDER BY Username", []);
    }
    [Fact]
    public void Example3_BooleanToggle_Provided() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE /*ActiveOnly*/Active = 1 ORDER BY Username");
        var builder = query.StartBuilder();
        builder.Use("ActiveOnly");
        Verify(builder, "SELECT ID, Username, Email FROM Users WHERE Active = 1 ORDER BY Username", []);
    }

    [Fact]
    public void Example4_FunctionalFootprint_NotProvided() {
        var query = new QueryCommand("SELECT ID, u.Name FROM Users u WHERE u.Name LIKE CONCAT('%', ?@Name, '%')");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT ID, u.Name FROM Users u", []);
    }
    [Fact]
    public void Example4_FunctionalFootprint_Provided() {
        var query = new QueryCommand("SELECT ID, u.Name FROM Users u WHERE u.Name LIKE CONCAT('%', ?@Name, '%')");
        var builder = query.StartBuilder();
        builder.Use("@Name", "Dev");
        Verify(builder, "SELECT ID, u.Name FROM Users u WHERE u.Name LIKE CONCAT('%', @Name, '%')", [("@Name", "Dev")]);
    }

    [Fact]
    public void Example5_ImplicitAnd_PartialProvided() {
        var query = new QueryCommand("SELECT ID, Name FROM Products WHERE Price * ?@Modifier > ?@Minimum");
        var builder = query.StartBuilder();
        builder.Use("@Modifier", 1.1);
        // @Minimum is missing, segment fails
        Verify(builder, "SELECT ID, Name FROM Products", [("@Modifier", 1.1)]);
    }
    [Fact]
    public void Example5_ImplicitAnd_AllProvided() {
        var query = new QueryCommand("SELECT ID, Name FROM Products WHERE Price * ?@Modifier > ?@Minimum");
        var builder = query.StartBuilder();
        builder.Use("@Modifier", 1.5);
        builder.Use("@Minimum", 10.0);
        Verify(builder, "SELECT ID, Name FROM Products WHERE Price * @Modifier > @Minimum", [("@Modifier", 1.5), ("@Minimum", 10.0)]);
    }

    [Fact]
    public void Example6_ContextJoining_NotProvided() {
        var query = new QueryCommand("SELECT * FROM Products WHERE Price IS NOT NULL &AND Price > ?@MinPrice");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT * FROM Products", []);
    }
    [Fact]
    public void Example6_ContextJoining_Provided() {
        var query = new QueryCommand("SELECT * FROM Products WHERE Price IS NOT NULL &AND Price > ?@MinPrice");
        var builder = query.StartBuilder();
        builder.Use("@MinPrice", 50);
        Verify(builder, "SELECT * FROM Products WHERE Price IS NOT NULL AND Price > @MinPrice", [("@MinPrice", 50)]);
    }

    [Fact]
    public void Example7_SectionToggle_NotProvided() {
        var template = "SELECT p.ID, p.Name FROM Products p /*@VendorName*/INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = ?@VendorName";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        Verify(builder, "SELECT p.ID, p.Name FROM Products p WHERE p.IsActive = 1", []);
    }
    [Fact]
    public void Example7_SectionToggle_Provided() {
        var query = new QueryCommand("SELECT p.ID, p.Name FROM Products p /*@VendorName*/INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = ?@VendorName");
        var builder = query.StartBuilder();
        builder.Use("@VendorName", "Microsoft");
        Verify(builder, "SELECT p.ID, p.Name FROM Products p INNER JOIN Vendors v ON v.ID = p.VendorID WHERE p.IsActive = 1 AND v.VendorName = @VendorName", [("@VendorName", "Microsoft")]);
    }

    [Fact]
    public void Example8_LinearLogic_AndGate_Partial() {
        var query = new QueryCommand("SELECT ID, Username, Email, /*Internal&Authorized*/SocialSecurityNumber FROM Users");
        var builder = query.StartBuilder();
        builder.Use("Internal");
        // Authorized missing
        Verify(builder, "SELECT ID, Username, Email FROM Users", []);
    }
    [Fact]
    public void Example8_LinearLogic_AndGate_Partial2() {
        var query = new QueryCommand("SELECT ID, Username, Email, /*Internal&Authorized*/SocialSecurityNumber FROM Users");
        var builder = query.StartBuilder();
        builder.Use("Authorized");
        // Internal missing
        Verify(builder, "SELECT ID, Username, Email FROM Users", []);
    }
    [Fact]
    public void Example8_LinearLogic_AndGate_Full() {
        var query = new QueryCommand("SELECT ID, Username, Email, /*Internal&Authorized*/SocialSecurityNumber FROM Users");
        var builder = query.StartBuilder();
        builder.Use("Internal");
        builder.Use("Authorized");
        Verify(builder, "SELECT ID, Username, Email, SocialSecurityNumber FROM Users", []);
    }

    [Fact]
    public void Example9_AtomicSubquery_NotProvided() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE /*@ActionType*/(SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT ID, Name FROM Users", []);
    }
    [Fact]
    public void Example9_AtomicSubquery_Provided() {
        var query = new QueryCommand("SELECT ID, Name FROM Users WHERE /*@ActionType*/(SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0");
        var builder = query.StartBuilder();
        builder.Use("@ActionType", 2);
        Verify(builder, "SELECT ID, Name FROM Users WHERE (SELECT Count(*) FROM Actions WHERE UserID = ID AND Type = @ActionType) > 0", [("@ActionType", 2)]);
    }

    private static readonly string[] MultipleValues = ["Test1", "Test2"];
    [Fact]
    public void Example10_CollectionHandler_X() {
        var query = new QueryCommand("SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)");
        var builder = query.StartBuilder();
        builder.Use("@Cats", MultipleValues);
        Verify(builder, "SELECT * FROM Tasks WHERE CategoryID IN (@Cats_1, @Cats_2)", [("@Cats_1", "Test1"), ("@Cats_2", "Test2")]);
    }
    [Fact]
    public void Example10_CollectionHandler_X_Many() {
        var query = new QueryCommand("SELECT * FROM Tasks WHERE CategoryID IN (?@Cats_X)");
        var builder = query.StartBuilder();
        var amount = 150;
        builder.Use("@Cats", Enumerable.Range(1, amount));
        var expectedParams = Enumerable.Range(1, amount).Select(i => ("@Cats_" + i, (object)i)).ToArray();
        Verify(builder, $"SELECT * FROM Tasks WHERE CategoryID IN ({string.Join(", ", expectedParams.Select(t => t.Item1))})", expectedParams);
    }

    [Fact]
    public void Example11_Missing() {
        var query = new QueryCommand("SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT Name FROM Products ORDER BY ID", []);
    }

    [Fact]
    public void Example11_PassengerDependency_MissingRequired() {
        var query = new QueryCommand("SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY");
        var builder = query.StartBuilder();
        builder.Use("@Skip", 50);
        // @Take is not provided but is required inside the activated segment
        var cmd = DummyCnn.CreateDummyCommand();
        Assert.Throws<RequiredHandlerValueException>(() => builder.QueryCommand.SetCommand(cmd, builder.Variables));
    }

    [Fact]
    public void Example11_Complete() {
        var query = new QueryCommand("SELECT Name FROM Products ORDER BY ID OFFSET ?@Skip_N ROWS FETCH NEXT @Take_N ROWS ONLY");
        var builder = query.StartBuilder();
        builder.Use("@Skip", 50);
        builder.Use("@Take", 50);
        Verify(builder, "SELECT Name FROM Products ORDER BY ID OFFSET 50 ROWS FETCH NEXT 50 ROWS ONLY", []);
    }
    [Fact]
    public void Example12_RawInjection_R() {
        var query = new QueryCommand("SELECT ID, Name FROM @Table_R WHERE IsActive = 1 AND Name = @Name_S");
        var builder = query.StartBuilder();
        builder.Use("@Table", "Logs");
        builder.Use("@Name", "Name");
        Verify(builder, "SELECT ID, Name FROM Logs WHERE IsActive = 1 AND Name = 'Name'", []);
    }

    [Fact]
    public void Example13_DynamicProjection_AggNotProvided() {
        var template = "SELECT /*Agg*/COUNT(*) AS Total&, SUM(Price) AS Revenue, p.CategoryName, /*NotAgg*/p.BrandName&, p.ID FROM Products p WHERE p.IsActive = 1 /*Agg*/GROUP BY p.CategoryName, p.BrandName";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("NotAgg");
        Verify(builder, "SELECT p.CategoryName, p.BrandName, p.ID FROM Products p WHERE p.IsActive = 1", []);
    }
    [Fact]
    public void Example13_DynamicProjection_AggProvided() {
        var template = "SELECT /*Agg*/COUNT(*) AS Total&, SUM(Price) AS Revenue, p.CategoryName, /*NotAgg*/p.BrandName&, p.ID FROM Products p WHERE p.IsActive = 1 /*Agg*/GROUP BY p.CategoryName, p.BrandName";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("Agg");
        // NotAgg is missing, but Agg is present
        Verify(builder, "SELECT COUNT(*) AS Total, SUM(Price) AS Revenue, p.CategoryName FROM Products p WHERE p.IsActive = 1 GROUP BY p.CategoryName, p.BrandName", []);
    }
    [Fact]
    public void Example13_DynamicProjection_BothProvided() {
        var template = "SELECT /*Agg*/COUNT(*) AS Total&, SUM(Price) AS Revenue, p.CategoryName, /*NotAgg*/p.BrandName&, p.ID FROM Products p WHERE p.IsActive = 1 /*Agg*/GROUP BY p.CategoryName, p.BrandName";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("Agg");
        builder.Use("NotAgg");
        Verify(builder, "SELECT COUNT(*) AS Total, SUM(Price) AS Revenue, p.CategoryName, p.BrandName, p.ID FROM Products p WHERE p.IsActive = 1 GROUP BY p.CategoryName, p.BrandName", []);
    }

    [Fact]
    public void Example14_ColumnJoining() {
        var query = new QueryCommand("SELECT ID, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM Users");
        var builder = query.StartBuilder();
        builder.Use("IncludeAddress");
        Verify(builder, "SELECT ID, Username, City, Street, ZipCode FROM Users", []);
    }

    [Fact]
    public void Example14_ColumnJoining_Missing() {
        var query = new QueryCommand("SELECT ID, Username, /*IncludeAddress*/City&, Street&, ZipCode FROM Users");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT ID, Username FROM Users", []);
    }

    [Fact]
    public void Example15_UpdateListCleanup() {
        var template = "UPDATE Users SET LastModified = GETDATE(), Username = ?@Username, Email = ?@Email WHERE ID = @ID";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Username", "jdoe");
        builder.Use("@ID", 1);
        // @Email missing
        Verify(builder, "UPDATE Users SET LastModified = GETDATE(), Username = @Username WHERE ID = @ID", [("@Username", "jdoe"), ("@ID", 1)]);
    }
    [Fact]
    public void Example15_UpdateListCleanup_AllProvided() {
        var template = "UPDATE Users SET LastModified = GETDATE(), Username = ?@Username, Email = ?@Email WHERE ID = @ID";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Username", "jdoe");
        builder.Use("@Email", "j@doe.com");
        builder.Use("@ID", 1);
        Verify(builder, "UPDATE Users SET LastModified = GETDATE(), Username = @Username, Email = @Email WHERE ID = @ID", [("@Username", "jdoe"), ("@Email", "j@doe.com"), ("@ID", 1)]);
    }
    [Fact]
    public void Example15_UpdateListCleanup_MissingID() {
        var template = "UPDATE Users SET LastModified = GETDATE(), Username = ?@Username, Email = ?@Email WHERE ID = @ID";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Username", "jdoe");
        // @Email missing
        Verify(builder, "UPDATE Users SET LastModified = GETDATE(), Username = @Username WHERE ID = @ID", [("@Username", "jdoe")]);
    }

    [Fact]
    public void Example16_InsertColumnDependency() {
        var template = "INSERT INTO Users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Username", "alice");
        Verify(builder, "INSERT INTO Users (Username) VALUES (@Username)", [("@Username", "alice")]);
    }
    [Fact]
    public void Example16_InsertColumnDependency_Provided() {
        var template = "INSERT INTO Users (Username, /*@Email*/Email) VALUES (@Username, ?@Email)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Username", "alice");
        builder.Use("@Email", "a@a.com");
        Verify(builder, "INSERT INTO Users (Username, Email) VALUES (@Username, @Email)", [("@Username", "alice"), ("@Email", "a@a.com")]);
    }

    [Fact]
    public void Example17_MultiColumnInsert() {
        var template = "INSERT INTO Profiles (UserID, /*Details*/Bio&, Website&, AvatarURL) VALUES (@UID, /*Details*/@Bio&, @Web&, @Img)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("Details");
        builder.Use("@UID", 5);
        builder.Use("@Bio", "Hi");
        builder.Use("@Web", "site.com");
        builder.Use("@Img", "pic.png");
        Verify(builder, "INSERT INTO Profiles (UserID, Bio, Website, AvatarURL) VALUES (@UID, @Bio, @Web, @Img)",
            [("@UID", 5), ("@Bio", "Hi"), ("@Web", "site.com"), ("@Img", "pic.png")]);
    }
    [Fact]
    public void Example17_MultiColumnInsert_Provided() {
        var template = "INSERT INTO Profiles (UserID, /*Details*/Bio&, Website&, AvatarURL) VALUES (@UID, /*Details*/@Bio&, @Web&, @Img)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("Details");
        builder.Use("@UID", 5);
        builder.Use("@Bio", "Hi");
        builder.Use("@Web", "site.com");
        builder.Use("@Img", "pic.png");
        Verify(builder, "INSERT INTO Profiles (UserID, Bio, Website, AvatarURL) VALUES (@UID, @Bio, @Web, @Img)", [("@UID", 5), ("@Bio", "Hi"), ("@Web", "site.com"), ("@Img", "pic.png")]);
    }
    [Fact]
    public void Example17_MultiColumnInsert_Alternative_Template() {
        var template = "INSERT INTO Profiles (UserID, /*@Bio&@Web&@Img*/Bio&, Website&, AvatarURL) VALUES (@UID, ?@Bio&, ?@Web&, ?@Img)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Bio", "Hi");
        builder.Use("@Web", "site.com");
        builder.Use("@UID", 5);
        Verify(builder, "INSERT INTO Profiles (UserID) VALUES (@UID)", [("@UID", 5), ("@Bio", "Hi"), ("@Web", "site.com")]);
    }
    [Fact]
    public void Example17_MultiColumnInsert_Alternative_Template_Provided() {
        var template = "INSERT INTO Profiles (UserID, /*@Bio&@Web&@Img*/Bio&, Website&, AvatarURL) VALUES (@UID, ?@Bio&, ?@Web&, ?@Img)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Bio", "Hi");
        builder.Use("@Web", "site.com");
        builder.Use("@Img", "pic.png");
        builder.Use("@UID", 5);
        Verify(builder, "INSERT INTO Profiles (UserID, Bio, Website, AvatarURL) VALUES (@UID, @Bio, @Web, @Img)", [("@UID", 5), ("@Bio", "Hi"), ("@Web", "site.com"), ("@Img", "pic.png")]);
    }

    [Fact]
    public void Example18_DeleteAndCleanup() {
        var template = "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND /*PurgeOldOnly*/IsArchived = 1";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        Verify(builder, "DELETE FROM Logs WHERE LogDate < GETDATE() - 30", []);
    }
    [Fact]
    public void Example18_DeleteAndCleanup_Provided() {
        var template = "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND /*PurgeOldOnly*/IsArchived = 1";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("PurgeOldOnly");
        Verify(builder, "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND IsArchived = 1", []);
    }

    [Fact]
    public void Example18_DeleteAndCleanup_Alternative() {
        var template = "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND (/*PurgeFromAll*/1=1 OR IsArchived = 1)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        Verify(builder, "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND ( IsArchived = 1)", []);
    }
    [Fact]
    public void Example18_DeleteAndCleanup_Alternative_Provided() {
        var template = "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND (/*PurgeFromAll*/1=1 OR IsArchived = 1)";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("PurgeFromAll");
        Verify(builder, "DELETE FROM Logs WHERE LogDate < GETDATE() - 30 AND (1=1 OR IsArchived = 1)", []);
    }
    [Fact]
    public void Example19_DynamicOrderBy_Cleanup() {
        var template = "SELECT * FROM Products WHERE IsActive = 1 /*@Sort*/ORDER BY @Sort_R ?@Dir_R";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Sort", "Price");
        // @Dir is missing, footprint @Sort_R @Dir_R fails, leaving ORDER BY empty
        Verify(builder, "SELECT * FROM Products WHERE IsActive = 1", []);
    }
    [Fact]
    public void Example19_DynamicOrderBy_FullyProvided() {
        var template = "SELECT * FROM Products WHERE IsActive = 1 /*@Sort*/ORDER BY @Sort_R ?@Dir_R";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@Sort", "Price");
        builder.Use("@Dir", "DESC");
        Verify(builder, "SELECT * FROM Products WHERE IsActive = 1 ORDER BY Price DESC", []);
    }
    [Fact]
    public void Case_NotProvided() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        Verify(builder, "SELECT * FROM Products", []);
    }
    [Fact]
    public void Case_Main_Not_Provided() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        Verify(builder, "SELECT * FROM Products WHERE CASE THEN 1 WHEN Category = 0 ELSE 0 END = @CatFlag", [("@CatFlag", 1)]);
    }
    [Fact]
    public void Case_Main_Provided() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category /*@Category*/THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        Verify(builder, "SELECT * FROM Products WHERE CASE WHEN Category = 0 ELSE 0 END = @CatFlag", [("@CatFlag", 1)]);
    }
    [Fact]
    public void Case_One_Provided() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        builder.Use("@Category", 1);
        Verify(builder, "SELECT * FROM Products WHERE CASE WHEN Category = @Category THEN 1 WHEN Category = 0 ELSE 0 END = @CatFlag", [("@Category", 1), ("@CatFlag", 1)]);
    }
    [Fact]
    public void Case_One_Provided_ActualUse() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 /*@NoCategory*/WHEN Category = 0 THEN ?@NoCategory_S ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        builder.Use("@Category", 1);
        Verify(builder, "SELECT * FROM Products WHERE CASE WHEN Category = @Category THEN 1 ELSE 0 END = @CatFlag", [("@Category", 1), ("@CatFlag", 1)]);
    }
    [Fact]
    public void Case_All_Provided() {
        var template = "SELECT * FROM Products WHERE CASE WHEN Category = ?@Category THEN 1 WHEN Category = 0 THEN ?@NoCategory_N ELSE 0 END = ?@CatFlag";
        var query = new QueryCommand(template);
        var builder = query.StartBuilder();
        builder.Use("@CatFlag", 1);
        builder.Use("@Category", 1);
        builder.Use("@NoCategory", -1);
        Verify(builder, "SELECT * FROM Products WHERE CASE WHEN Category = @Category THEN 1 WHEN Category = 0 THEN -1 ELSE 0 END = @CatFlag", [("@Category", 1), ("@CatFlag", 1)]);
    }
    [Fact]
    public void Pure_String() {
        var query = new QueryCommand("SELECT ID, Username, Email FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        Verify(builder, "SELECT ID, Username, Email FROM Users WHERE IsActive = 1", []);
    }
    [Fact]
    public void Extract_Select_None() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        Verify(builder, " FROM Users WHERE IsActive = 1", []);
    }
    [Fact]
    public void Extract_Select_None_Union() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1 UNION ALL ?SELECT ID, Username, Email&, Test FROM Other");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Test");
        Verify(builder, "SELECT ID, Email, Test FROM Users WHERE IsActive = 1 UNION ALL SELECT ID, Email, Test FROM Other", []);
    }

    [Fact]
    public void Extract_Select_One() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("ID");
        Verify(builder, "SELECT ID FROM Users WHERE IsActive = 1", []);
    }
    [Fact]
    public void Extract_Select_Part_Join() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Test");
        Verify(builder, "SELECT ID, Email, Test FROM Users WHERE IsActive = 1", []);
    }
    [Fact]
    public void Extract_Select_Part_Join2() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Test");
        Verify(builder, "SELECT ID, Email, Test FROM Users WHERE IsActive = 1", []);
    }
    [Fact]
    public void Extract_Select_All() {
        var query = new QueryCommand("?SELECT ID, Username, Email&, Test FROM Users WHERE IsActive = 1");
        var builder = query.StartBuilder();
        builder.Use("Test");
        builder.Use("Username");
        builder.Use("ID");
        Verify(builder, "SELECT ID, Username, Email, Test FROM Users WHERE IsActive = 1", []);
    }
    [Fact]
    public void Extract_Select_Cte() {
        var query = new QueryCommand("WITH U AS (?SELECT ID, Name, Salary FROM Users) SELECT * FROM U");
        var builder = query.StartBuilder();
        builder.Use("Name");
        Verify(builder, "WITH U AS (SELECT Name FROM Users) SELECT * FROM U", []);
    }
    [Fact]
    public void Using_ObjectMapping_Struct_Obj() {
        var query = new QueryCommand("SELECT EmployeeId, FirstName, Salary, /*Year*/Year FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName AND Status = ?@EmployeeStatus ORDER BY Salary DESC");
        var builder = query.StartBuilder();
        builder.UseWith((object)new TestDtoStruct(10, null, "P"));
        Verify(builder, "SELECT EmployeeId, FirstName, Salary FROM Employees WHERE Salary >= @MinSalary AND Status = @EmployeeStatus ORDER BY Salary DESC",
            [("@MinSalary", 10), ("@EmployeeStatus", "P")]);

    }
    [Fact]
    public void Using_ObjectMapping_Class_Obj() {
        var query = new QueryCommand("SELECT EmployeeId, FirstName, Salary, /*Year*/Year FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName AND Status = ?@EmployeeStatus ORDER BY Salary DESC");
        var builder = query.StartBuilder();

        builder.UseWith((object)new TestDtoClass(null, null, null) { Year = true });
        Verify(builder, "SELECT EmployeeId, FirstName, Salary, Year FROM Employees ORDER BY Salary DESC", []);

    }
    [Fact]
    public void Using_ObjectMapping_Struct_Ref() {
        var query = new QueryCommand("SELECT EmployeeId, FirstName, Salary, /*Year*/Year FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName AND Status = ?@EmployeeStatus ORDER BY Salary DESC");
        var builder = query.StartBuilder();
        var t = new TestDtoStruct(null, "Marketing", "Employed");
        builder.UseWith(ref t);
        Verify(builder, "SELECT EmployeeId, FirstName, Salary FROM Employees WHERE Department = @DeptName AND Status = @EmployeeStatus ORDER BY Salary DESC",
            [("@DeptName", "Marketing"), ("@EmployeeStatus", "Employed")]);
    }
    [Fact]
    public void Using_ObjectMapping_Class_Ref() {
        var query = new QueryCommand("SELECT EmployeeId, FirstName, Salary, /*Year*/Year FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName AND Status = ?@EmployeeStatus ORDER BY Salary DESC");
        var builder = query.StartBuilder();
        var t = new TestDtoClass(22, "Marketingg", "Employed") { Year = true };
        builder.UseWith(ref t);
        Verify(builder, "SELECT EmployeeId, FirstName, Salary, Year FROM Employees WHERE Salary >= @MinSalary AND Department = @DeptName AND Status = @EmployeeStatus ORDER BY Salary DESC",
            [("@MinSalary", 22), ("@DeptName", "Marketingg"), ("@EmployeeStatus", "Employed")]);
    }
    [Fact]
    public void Using_ObjectMapping_Struct() {
        var query = new QueryCommand("SELECT EmployeeId, FirstName, Salary, /*Year*/Year FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName AND Status = ?@EmployeeStatus ORDER BY Salary DESC");
        var builder = query.StartBuilder();
        var t = new TestDtoStruct(null, "Marketing", "Employed");
        builder.UseWith(t);
        Verify(builder, "SELECT EmployeeId, FirstName, Salary FROM Employees WHERE Department = @DeptName AND Status = @EmployeeStatus ORDER BY Salary DESC",
            [("@DeptName", "Marketing"), ("@EmployeeStatus", "Employed")]);
    }
    [Fact]
    public void Using_ObjectMapping_Class() {
        var query = new QueryCommand("SELECT EmployeeId, FirstName, Salary, /*Year*/Year FROM Employees WHERE Salary >= ?@MinSalary AND Department = ?@DeptName AND Status = ?@EmployeeStatus ORDER BY Salary DESC");
        var builder = query.StartBuilder();
        var t = new TestDtoClass(23, "Marketin", null) { Year = true };
        builder.UseWith(t);
        Verify(builder, "SELECT EmployeeId, FirstName, Salary, Year FROM Employees WHERE Salary >= @MinSalary AND Department = @DeptName ORDER BY Salary DESC",
            [("@MinSalary", 23), ("@DeptName", "Marketin")]);
    }
    [Fact]
    public void Extract_Select_Implicit_And() {
        var query = new QueryCommand("?SELECT ID, Username, /*#Admin*/Email FROM Users");
        var builder = query.StartBuilder();
        builder.Use("ID");
        builder.Use("Username");
        builder.Use("Email");
        Verify(builder, "SELECT ID, Username FROM Users", []);
    }
}
public record struct TestDtoStruct(int? MinSalary, string? DeptName, string? EmployeeStatus);

public record class TestDtoClass(int? MinSalary, string? DeptName, string? EmployeeStatus) {
    public int OtherField = 32;
    [ForBoolCond] public bool Year;
}