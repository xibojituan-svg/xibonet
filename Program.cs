using Microsoft.AspNetCore.Mvc;
using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;
using XiboNetBackend.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 从配置文件中读取数据库连接字符串
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
// 使用依赖注入，将 SqlConnection 交给系统管理
builder.Services.AddTransient<IDbConnection>((sp) => new SqlConnection(connectionString!));

var app = builder.Build();

// 强制开启 Swagger (即使当前环境显示为 Production 也能访问测试界面)
app.UseSwagger();
app.UseSwaggerUI();

// 临时注释掉 HTTPS 重定向，彻底解决你看到的警告 (warn) 问题
// app.UseHttpsRedirection();

// ---- 接口1: 获取所有历史诊断记录 (Dapper Query) ----
app.MapGet("/api/finance/history", async (IDbConnection db) =>
{
    // Dapper 魔法：极致性能的 SQL 查询，自动转换结果
    var records = await db.QueryAsync("SELECT * FROM ChannelRoiDiagnosis ORDER BY CreatedAt DESC");
    return Results.Ok(records);
})
.WithName("GetDiagnosisHistory")
.WithOpenApi();


// ---- 接口2: 诊断并写入本地 SQL 数据库 (Dapper Insert) ----
app.MapPost("/api/finance/ue-diagnosis", async ([FromBody] UeRequest request, IDbConnection db) =>
{
    // 基础 UE 逻辑计算
    decimal ltv = request.AverageOrderValue * request.ConversionRate;
    decimal cac = request.MarketingCost / (request.LeadsCount > 0 ? request.LeadsCount : 1);
    
    decimal roi = cac > 0 ? ltv / cac : 0;
    string healthStatus = roi >= 3 ? "健康 (A)" : (roi >= 1 ? "风险 (B/C)" : "极危 (D/熔断)");
    string recommendation = roi < 1 ? "立即切断投放信号" : "持续小幅放量测试";

    // 使用 Dapper 执行原生 SQL，参数化防止注入
    string sql = @"
        INSERT INTO ChannelRoiDiagnosis (Channel, Ltv, Cac, Roi, Status, Recommendation) 
        VALUES (@Channel, @Ltv, @Cac, @Roi, @Status, @Recommendation);
        SELECT CAST(SCOPE_IDENTITY() as int);
    ";

    var parameters = new {
        Channel = request.ChannelName,
        Ltv = Math.Round(ltv, 2),
        Cac = Math.Round(cac, 2),
        Roi = Math.Round(roi, 2),
        Status = healthStatus,
        Recommendation = recommendation
    };

    // 写入数据库并获取生成的自增 ID
    var id = await db.QuerySingleAsync<int>(sql, parameters);

    return Results.Ok(new
    {
        Id = id,
        Message = "诊断记录已成功存入本地原生 SQL Server 数据库!",
        Report = parameters
    });
})
.WithName("UeDiagnosisAndSave")
.WithOpenApi();


// ---- 接口3: 删除测试数据 (Dapper Execute) ----
app.MapDelete("/api/finance/delete/{id}", async (int id, IDbConnection db) =>
{
    // Dapper 执行原生的 DELETE，@Id 防止 SQL 注入
    string sql = "DELETE FROM ChannelRoiDiagnosis WHERE Id = @Id";
    
    // ExecuteAsync 专用于执行 INSERT/UPDATE/DELETE 并返回受影响的行数
    int affectedRows = await db.ExecuteAsync(sql, new { Id = id });
    
    if(affectedRows > 0)
    {
        return Results.Ok(new { Message = $"太棒了！流水号为 {id} 的测试数据已成功从数据库中彻底抹除！" });
    }
    
    return Results.NotFound(new { Message = $"未找到 ID 为 {id} 的记录，可能已经被删除了。" });
})
.WithName("DeleteDiagnosisData")
.WithOpenApi();


// ==========================================
// 🔐 实际业务功能：用户认证模块 (Auth)
// ==========================================

// 鉴权辅助：内存 Token 字典（生产环境应使用 JWT 或 Redis Session）
var AdminTokens = new Dictionary<string, string>();

// ---- 提供一个极简密码哈希的本地方法 (模拟生产环境的安全性) ----
static string HashPassword(string password)
{
    // 利用微软原生框架进行不可逆加密，绝不将明文存入数据库
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password + "XiboSafeSalt"));
    return Convert.ToBase64String(bytes);
}

// ---- 接口4: 注册功能 (Dapper SQL Insert) ----
app.MapPost("/api/auth/register", async ([FromBody] UserRegisterRequest request, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { Message = "用户名或密码不能为空！" });
    }

    // 检查用户名是否已存在
    int existCount = await db.QueryFirstOrDefaultAsync<int>("SELECT COUNT(1) FROM Users WHERE Username = @Username", new { Username = request.Username });
    if (existCount > 0)
    {
        return Results.BadRequest(new { Message = "该用户名已被注册，请换一个重试！" });
    }

    // 存入不可逆加密的 Hash 值
    string hashedPwd = HashPassword(request.Password);

    string sql = @"
        INSERT INTO Users (Username, PasswordHash) VALUES (@Username, @PasswordHash);
        SELECT CAST(SCOPE_IDENTITY() as int);
    ";
    
    int newId = await db.QuerySingleAsync<int>(sql, new { Username = request.Username, PasswordHash = hashedPwd });

    return Results.Ok(new { Message = "注册成功！你已正式成为最新系统的首批用户。", UserId = newId });
})
.WithName("RegisterUser")
.WithOpenApi();

// ---- 接口5: 登录功能 (返回简易 Token) ----
app.MapPost("/api/auth/login", async ([FromBody] UserLoginRequest request, IDbConnection db) =>
{
    string hashedPwd = HashPassword(request.Password);
    string sql = "SELECT Id, Username FROM Users WHERE Username = @Username AND PasswordHash = @PasswordHash";
    var user = await db.QueryFirstOrDefaultAsync<User>(sql, new { Username = request.Username, PasswordHash = hashedPwd });

    if (user != null)
    {
        // 生成一个极简 Token（用户名+时间戳的哈希），用于后续接口鉴权
        string token = HashPassword(user.Username + DateTime.Now.Ticks.ToString());
        // 将 Token 临时存入内存字典（生产环境应使用 Redis）
        AdminTokens[token] = user.Username;

        return Results.Ok(new { 
            Message = "登录成功", 
            Token = token, 
            UserInfo = new { user.Id, user.Username } 
        });
    }
    return Results.Unauthorized();
})
.WithName("LoginUser")
.WithOpenApi();


// ==========================================
// 📰 新闻资讯模块 (News CRUD)
// ==========================================



// 校验 Token 的本地方法
static bool IsAdmin(HttpContext ctx, Dictionary<string, string> tokens)
{
    string? authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader)) return false;
    string token = authHeader.Replace("Bearer ", "");
    return tokens.ContainsKey(token);
}

// ---- 接口6: 公开 - 获取新闻列表 (不需要登录) ----
app.MapGet("/api/news", async (IDbConnection db) =>
{
    var list = await db.QueryAsync<NewsArticle>("SELECT Id, Title, Summary, Author, CoverUrl, CreatedAt FROM News ORDER BY CreatedAt DESC");
    return Results.Ok(list);
})
.WithName("GetNewsList")
.WithOpenApi();

// ---- 接口7: 公开 - 获取单篇新闻详情 (不需要登录) ----
app.MapGet("/api/news/{id}", async (int id, IDbConnection db) =>
{
    var article = await db.QueryFirstOrDefaultAsync<NewsArticle>("SELECT * FROM News WHERE Id = @Id", new { Id = id });
    if (article == null) return Results.NotFound(new { Message = "文章不存在" });
    return Results.Ok(article);
})
.WithName("GetNewsDetail")
.WithOpenApi();

// ---- 接口8: 管理员 - 新增新闻 ----
app.MapPost("/api/admin/news", async (HttpContext ctx, [FromBody] NewsCreateRequest request, IDbConnection db) =>
{
    if (!IsAdmin(ctx, AdminTokens)) return Results.Unauthorized();

    string sql = @"
        INSERT INTO News (Title, Summary, Content, Author, CoverUrl) 
        VALUES (@Title, @Summary, @Content, @Author, @CoverUrl);
        SELECT CAST(SCOPE_IDENTITY() as int);";
    int newId = await db.QuerySingleAsync<int>(sql, request);
    return Results.Ok(new { Message = "新闻发布成功", Id = newId });
})
.WithName("CreateNews")
.WithOpenApi();

// ---- 接口9: 管理员 - 修改新闻 ----
app.MapPut("/api/admin/news/{id}", async (HttpContext ctx, int id, [FromBody] NewsCreateRequest request, IDbConnection db) =>
{
    if (!IsAdmin(ctx, AdminTokens)) return Results.Unauthorized();

    string sql = @"
        UPDATE News SET Title=@Title, Summary=@Summary, Content=@Content, Author=@Author, CoverUrl=@CoverUrl, UpdatedAt=GETDATE()
        WHERE Id=@Id";
    int rows = await db.ExecuteAsync(sql, new { Id = id, request.Title, request.Summary, request.Content, request.Author, request.CoverUrl });
    if (rows == 0) return Results.NotFound(new { Message = "文章不存在" });
    return Results.Ok(new { Message = "更新成功" });
})
.WithName("UpdateNews")
.WithOpenApi();

// ---- 接口10: 管理员 - 删除新闻 ----
app.MapDelete("/api/admin/news/{id}", async (HttpContext ctx, int id, IDbConnection db) =>
{
    if (!IsAdmin(ctx, AdminTokens)) return Results.Unauthorized();

    int rows = await db.ExecuteAsync("DELETE FROM News WHERE Id = @Id", new { Id = id });
    if (rows == 0) return Results.NotFound(new { Message = "文章不存在" });
    return Results.Ok(new { Message = "删除成功" });
})
.WithName("DeleteNews")
.WithOpenApi();


// 启用静态文件服务（让 wwwroot 目录下的 HTML/CSS/JS 可以被直接访问）
app.UseStaticFiles();

app.Run();

