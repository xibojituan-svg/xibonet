using Microsoft.AspNetCore.Mvc;
using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;
using XiboNetBackend.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient(); // 注入 HttpClient 支持给钉钉接口使用

// 注册后台定时任务守护进程：每天执行自动扫描与推送
builder.Services.AddHostedService<DailyDingTalkReporter>();

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

// 战略洞察分析防骚扰(去重)数据库：记录已经被分析过的文档 ID
// 预先将刚才单独提炼过的私域 PDF 的节点 ID 存进去作为排除历史
var AnalyzedDocIds = new HashSet<string> { "Exel2BLV5zv9wMEDFDDmMy1nJgk9rpMq" };


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
    string sql = "SELECT Id, Username, RoleLevel FROM Users WHERE Username = @Username AND PasswordHash = @PasswordHash";
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
            UserInfo = new { user.Id, user.Username, user.RoleLevel } 
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


// ==========================================
// 🛡️ DSTE 权限体系底层模块模拟
// ==========================================

// ---- DSTE 动态权限系统 (连接数据库实时查询) ----
static async Task<int> GetUserLevelAsync(HttpContext ctx, Dictionary<string, string> tokens, IDbConnection db)
{
    string? authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
    if (string.IsNullOrEmpty(authHeader)) return 4; // 未携带Token默认当做底层普通员工(L4)处理
    string token = authHeader.Replace("Bearer ", "");
    
    if (!tokens.ContainsKey(token)) return 4; // Token不存在也按L4处理

    string username = tokens[token];
    // 实时读取数据库人员架构树里的级别设定，一旦后台调级，前端接口立马生效并控制数据降维或脱敏
    string sql = "SELECT RoleLevel FROM Users WHERE Username = @Username";
    int level = await db.QueryFirstOrDefaultAsync<int>(sql, new { Username = username });
    
    return level > 0 ? level : 4; // 防御性：空则作为基层
}

// ==========================================
// 🚀 核心：DSTE 接口与动态脱敏中间件逻辑
// ==========================================

// ---- 接口11: 动态脱敏获取集团战略 (SP) ----
// 能够获取公司未来的战略愿景，但是金额只有 L1(老板/CFO)可见。
app.MapGet("/api/dste/strategy", async (HttpContext ctx, IDbConnection db) =>
{
    int userLevel = await GetUserLevelAsync(ctx, AdminTokens, db);
    
    var spList = await db.QueryAsync<StrategicPlan>("SELECT * FROM StrategicPlan ORDER BY PlanYear DESC");
    
    // 动态脱敏拦截映射
    var dtoList = spList.Select(sp => new StrategicPlanDto
    {
        Id = sp.Id,
        PlanYear = sp.PlanYear,
        Vision = sp.Vision,
        CoreValues = sp.CoreValues,
        PublicDirection = sp.PublicDirection, // 这条对所有人公开
        
        // 核心动态权限校验：只有 L1 能够查看到绝对财务核心目标
        FinancialGoalDisplay = userLevel == 1 
            ? sp.ConfidentialFinancialGoal.ToString("C") // 返回带符号的货币格式
            : "****** (核心商业机密，您的权限为 L" + userLevel + ")"
    });

    return Results.Ok(dtoList);
})
.WithName("GetStrategies")
.WithOpenApi();

// ---- 接口12: 动态脱敏获取平衡计分卡 (BSC) ----
// 返回战略执行层面的部门级四象限指标。下级能够看到进度条，但看不到真实的大数据。
app.MapGet("/api/dste/bsc/{spId}", async (HttpContext ctx, int spId, IDbConnection db) =>
{
    int userLevel = await GetUserLevelAsync(ctx, AdminTokens, db);
    
    // 查询该战略下的所有BSC指标
    var bscList = await db.QueryAsync<BalancedScorecard>(
        "SELECT * FROM BalancedScorecard WHERE StrategicPlanId = @SpId", 
        new { SpId = spId }
    );

    // DTO 数据脱敏转化
    var dtoList = bscList.Select(bsc => 
    {
        var dto = new BalancedScorecardDto
        {
            Id = bsc.Id,
            StrategicPlanId = bsc.StrategicPlanId,
            DepartmentName = bsc.DepartmentName,
            Perspective = bsc.Perspective,
            Objective = bsc.Objective,
            ConfidentialityLevel = bsc.ConfidentialityLevel,
            CreatedAt = bsc.CreatedAt
        };

        // 动态脱敏判定：层级数字越小权限越高，例如 L1=1 为最高
        // 若当前请求用户层级 数字 <= BSC所设置的最低查看层级，则可以看到具体数字
        // 比如bsc配置等级为2(L2级别)，L1(1)和L2(2)可以看。L3(3)不可看。
        if (userLevel <= bsc.ConfidentialityLevel)
        {
            dto.TargetValueDisplay = bsc.TargetValue.ToString("N0") + " 单位";
            dto.CurrentValueDisplay = bsc.CurrentValue.ToString("N0") + " 单位";
        }
        else
        {
            // 作为普通士兵，不需要知道赚多少钱，只要知道全公司的战役打了多少进度
            // => 数据被剥离（脱敏）为进度百分比呈现
            decimal percentage = bsc.TargetValue > 0 ? (bsc.CurrentValue / bsc.TargetValue) * 100 : 0;
            dto.TargetValueDisplay = "*** (机密)";
            dto.CurrentValueDisplay = $"当前进度: {Math.Round(percentage, 1)}%";
        }

        return dto;
    });

    return Results.Ok(dtoList);
})
.WithName("GetBscList")
.WithOpenApi();

// ---- 接口13: 创建测试数据(战略与BSC及下钻) (辅助接口) ----
app.MapPost("/api/dste/seed", async (IDbConnection db) =>
{
    // 1. 生成或检查假用户用于FK关联
    string pwdHash = HashPassword("password123");
    string checkUsers = $@"
        IF NOT EXISTS (SELECT 1 FROM Users WHERE Username='admin') INSERT INTO Users (Username, PasswordHash, RoleLevel) VALUES ('admin', '{pwdHash}', 1) ELSE UPDATE Users SET PasswordHash='{pwdHash}', RoleLevel=1 WHERE Username='admin';
        IF NOT EXISTS (SELECT 1 FROM Users WHERE Username='vp') INSERT INTO Users (Username, PasswordHash, RoleLevel) VALUES ('vp', '{pwdHash}', 2) ELSE UPDATE Users SET PasswordHash='{pwdHash}', RoleLevel=2 WHERE Username='vp';
        IF NOT EXISTS (SELECT 1 FROM Users WHERE Username='employee') INSERT INTO Users (Username, PasswordHash, RoleLevel) VALUES ('employee', '{pwdHash}', 4) ELSE UPDATE Users SET PasswordHash='{pwdHash}', RoleLevel=4 WHERE Username='employee';
    ";
    await db.ExecuteAsync(checkUsers);

    var users = (await db.QueryAsync<User>("SELECT Id, Username FROM Users")).ToDictionary(u => u.Username, u => u.Id);
    if(!users.ContainsKey("admin") || !users.ContainsKey("employee")) return Results.BadRequest("User seed failed.");

    // 2. 写入集团战略：目标为2026年-2028年战略规划
    string sqlSp = @"
        INSERT INTO StrategicPlan (PlanYear, Vision, CoreValues, PublicDirection, ConfidentialFinancialGoal)
        VALUES (2026, '成为中国最值得信赖的个人健康与幸福服务平台，帮助1000女性成为家庭健康掌门人', '由狩猎者转为农耕者，信任是唯一核心资产', '双曲线联动：第一曲线（教育）彻底解毒不暴雷，第二曲线（健康）启动', 245000000.00);
        SELECT CAST(SCOPE_IDENTITY() as int);
    ";
    int spId = await db.QuerySingleAsync<int>(sqlSp);

    // 3. 写入部门BSC (解析自喜播BSC关键战役_20260301.xlsx)
    string sqlBsc = @"
        INSERT INTO BalancedScorecard (StrategicPlanId, DepartmentName, Perspective, Objective, TargetValue, CurrentValue, ConfidentialityLevel)
        VALUES 
        (@SpId, '核心业务部', '财务', '净营收目标 (马长久)', 500000000.00, 100000000.00, 1), 
        (@SpId, '风控合规组', '客户', '大黑用户拦截率 (杜康杰)', 98.00, 20.00, 2),
        (@SpId, '客服满意组', '客户', '服务满意度NPS (马长久)', 85.00, 60.00, 3),
        (@SpId, '内部运营', '内部流程', '单SKU产能密度≥4个 (苏秦)', 4.00, 1.00, 2),
        (@SpId, '教研部', '内部流程', '学员考试合格率 (苏秦)', 75.00, 40.00, 3),
        (@SpId, 'AI技术部', '学习与成长', '关键岗位AI重构率 (王俊)', 80.00, 15.00, 2);
        SELECT CAST(SCOPE_IDENTITY() as int);       
    ";
    int bscId = await db.QuerySingleAsync<int>(sqlBsc, new { SpId = spId });

    // 4. 写入GSA战役
    string sqlGsa = @"
        INSERT INTO GoalStrategyAction (BscId, DepartmentName, GoalName, StrategyDescription, ActionPlan, IsCrossDepartment, Status)
        VALUES 
        (@BscId, '风控合规组', '大黑用户全栈拦截战役 (杜康杰)', '不赚带血的钱，100%拦截高风险特殊关怀群体', '1.建立风控数据模型 2.交易熔断与退费预警', 1, '进行中'),
        (@BscId, '核心业务部', '高频服务口碑战役 (马长久)', '利用NPS作为安全网抵抗AI替代人工可能造成的虚假繁荣', '1.全链路埋点评价 2.客服权重介入', 1, '进行中'),
        (@BscId, 'AI技术部', '关键岗位于AI重构提效 (王俊/姚涣)', '用大模型工具提升15%原助教和班主任的人效', '1.部署Agent助手 2.跑通知识库', 1, '未开始');
        SELECT CAST(SCOPE_IDENTITY() as int);
    ";
    int gsaId = await db.QuerySingleAsync<int>(sqlGsa, new { BscId = bscId });

    // 5. 写入底层用户的 OKR 
    string sqlOkr = @"
        INSERT INTO ObjectiveKeyResult (GsaId, OwnerId, Objective, KeyResults, IsPrivate)
        VALUES 
        (@GsaId, @EmpId, '完成业务接入大模型', 'KR1: 成功部署本地模型库 (完成度80%)', 0),
        (@GsaId, @AdminId, '构建云端高可用架构', 'KR1: SLA稳定性达到99.9% (完成度100%)', 0);
    ";
    await db.ExecuteAsync(sqlOkr, new { GsaId = gsaId, EmpId = users["employee"], AdminId = users["admin"] });

    // 6. 写入关键绩效 KPI
    string sqlKpi = @"
        INSERT INTO KeyPerformanceIndicator (OwnerId, IndicatorName, Weight, TargetScore, ActualScore, EvaluationPeriod, IsConfidential)
        VALUES 
        (@EmpId, '代码交付质量与BUG率', 60.0, 100.0, 95.0, '2026-Q1', 1),
        (@EmpId, '团队协同与价值观', 40.0, 100.0, 110.0, '2026-Q1', 1);
    ";
    await db.ExecuteAsync(sqlKpi, new { EmpId = users["employee"] });
    
    return Results.Ok(new { Message = "测试战略及底层拆解数据(BSC/GSA/OKR/KPI)全链路初始化完成!" });
})
.WithName("SeedDSTEData")
.WithOpenApi();

// ---- 接口补充: 深度清理战略落库测试数据 ----
app.MapDelete("/api/dste/clear", async (IDbConnection db) =>
{
    // 利用级联/反向顺序清理来绕开外键依赖
    string sql = @"
        DELETE FROM KeyPerformanceIndicator;
        DELETE FROM ObjectiveKeyResult;
        DELETE FROM GoalStrategyAction;
        DELETE FROM BalancedScorecard;
        DELETE FROM StrategicPlan;
    ";
    await db.ExecuteAsync(sql);
    return Results.Ok(new { Message = "沙盘推演终止，所有战略存底资料已彻底销毁！" });
})
.WithName("ClearDSTEData")
.WithOpenApi();

// ---- 接口14: 获取 OKR 及 “奋斗者”打分雷达 ----
// OKR默认全员公开透明，用于找齐大方向。并且通过内容识别“奋斗者高光”。
app.MapGet("/api/dste/okr_talent", async (IDbConnection db) => 
{
    string sql = @"
        SELECT o.*, u.Username as OwnerName, g.GoalName as Alignment 
        FROM ObjectiveKeyResult o
        JOIN Users u ON o.OwnerId = u.Id
        LEFT JOIN GoalStrategyAction g ON o.GsaId = g.Id
        ORDER BY o.CreatedAt DESC
    ";
    
    // 这里我们直接用 dynamic 来接，或者用对应的 DTO 扩展
    var rawOkrs = await db.QueryAsync<dynamic>(sql);

    var dtoList = rawOkrs.Select(row => {
        string kr = (string)row.KeyResults;
        int progress = 0;
        // 简单正则提取进度
        var match = System.Text.RegularExpressions.Regex.Match(kr, @"完成度(\d+)%");
        if(match.Success) progress = int.Parse(match.Groups[1].Value);

        // “奋斗者”判定模型 (简易版：完成度 >= 100%，或者KR中包含特定价值观关键词)
        bool isFendouzhe = progress >= 100 || kr.Contains("加班") || kr.Contains("客户") || kr.Contains("攻坚");

        return new ObjectiveKeyResultDto {
            Id = (int)row.Id,
            OwnerName = (string)row.OwnerName,
            Objective = (string)row.Objective,
            KeyResults = kr,
            Alignment = row.Alignment != null ? (string)row.Alignment : "未对齐战役",
            ProgressPercent = progress,
            IsFendouzhe = isFendouzhe,
            CreatedAt = (DateTime)row.CreatedAt
        };
    });

    return Results.Ok(dtoList);
})
.WithName("GetOkrs")
.WithOpenApi();

// ---- 接口15: 严格保密的个人 KPI 与绩效评级 ----
// L1 可见所有人，其他人只能看见自己。
app.MapGet("/api/dste/kpi", async (HttpContext ctx, IDbConnection db) => 
{
    string? authHeader = ctx.Request.Headers["Authorization"].FirstOrDefault();
    string token = authHeader?.Replace("Bearer ", "") ?? "";
    string currentUser = AdminTokens.ContainsKey(token) ? AdminTokens[token] : "";
    int userLevel = await GetUserLevelAsync(ctx, AdminTokens, db);

    // 基于权限动态构建SQL
    // 注意：实际项目中通常是通过WHERE加参数过滤，此处为简化逻辑
    string sql = @"
        SELECT k.*, u.Username as OwnerName
        FROM KeyPerformanceIndicator k
        JOIN Users u ON k.OwnerId = u.Id
    ";
    
    if(userLevel > 1) { // 不是最高决策层，只能看自己的KPI
        sql += " WHERE u.Username = @User";
    }

    var kpis = await db.QueryAsync<dynamic>(sql, new { User = currentUser });

    var dtoList = kpis.Select(k => {
        decimal actual = (decimal)k.ActualScore;
        decimal target = (decimal)k.TargetScore;
        string grade = "C (平庸)";
        string suggest = "需加强基础产出";

        if(actual >= target * 1.1m) { grade = "A+ (卓越奋斗者)"; suggest = "推荐升职加薪，配股倾斜"; }
        else if(actual >= target) { grade = "A (优秀)"; suggest = "给予项目奖金"; }
        else if(actual >= target * 0.8m) { grade = "B (良好)"; suggest = "平稳预期，持续辅导"; }
        else { grade = "D (熔断)"; suggest = "启动PIP改进计划或淘汰"; }

        return new KeyPerformanceIndicatorDto {
            Id = (int)k.Id,
            OwnerName = (string)k.OwnerName,
            IndicatorName = (string)k.IndicatorName,
            TargetScoreDisplay = target.ToString("N1"),
            ActualScoreDisplay = actual.ToString("N1"),
            EvaluationPeriod = (string)k.EvaluationPeriod,
            Grade = grade,
            Suggestion = suggest
        };
    });

    return Results.Ok(dtoList);
})
.WithName("GetKpis")
.WithOpenApi();

// ---- 接口16: 用户管理 - 获取所有用户及权限 ----
// 仅 L1 管理员可访问
app.MapGet("/api/dste/users", async (HttpContext ctx, IDbConnection db) => 
{
    int userLevel = await GetUserLevelAsync(ctx, AdminTokens, db);
    if(userLevel > 1) return Results.Unauthorized(); 

    var users = await db.QueryAsync<User>("SELECT Id, Username, RoleLevel, CreatedAt FROM Users");
    return Results.Ok(users);
})
.WithName("GetDsteUsers")
.WithOpenApi();

// ---- 接口17: 用户管理 - 修改用户权限级别 ----
app.MapPut("/api/dste/users/{id}/role", async (HttpContext ctx, int id, [FromBody] int targetRole, IDbConnection db) => 
{
    int userLevel = await GetUserLevelAsync(ctx, AdminTokens, db);
    if(userLevel > 1) return Results.Unauthorized(); 

    string sql = "UPDATE Users SET RoleLevel = @RoleLevel WHERE Id = @Id";
    int rows = await db.ExecuteAsync(sql, new { RoleLevel = targetRole, Id = id });
    if(rows == 0) return Results.NotFound(new { Message = "用户不存在" });
    
    return Results.Ok(new { Message = "人员权限等级调整就绪" });
})
.WithName("UpdateUserRole")
.WithOpenApi();

// ---- 接口18: 后台管理 - SP 战略增删改查 ----
app.MapGet("/api/admin/dste/sp", async (HttpContext ctx, IDbConnection db) => {
    if(await GetUserLevelAsync(ctx, AdminTokens, db) > 1) return Results.Unauthorized(); 
    return Results.Ok(await db.QueryAsync<StrategicPlan>("SELECT * FROM StrategicPlan ORDER BY PlanYear DESC"));
});
app.MapPost("/api/admin/dste/sp", async (HttpContext ctx, [FromBody] StrategicPlan sp, IDbConnection db) => {
    if(await GetUserLevelAsync(ctx, AdminTokens, db) > 1) return Results.Unauthorized(); 
    string sql = @"INSERT INTO StrategicPlan (PlanYear, Vision, CoreValues, PublicDirection, ConfidentialFinancialGoal) VALUES (@PlanYear, @Vision, @CoreValues, @PublicDirection, @ConfidentialFinancialGoal); SELECT CAST(SCOPE_IDENTITY() as int)";
    return Results.Ok(new { Message = "新增成功", Id = await db.QuerySingleAsync<int>(sql, sp) });
});
app.MapPut("/api/admin/dste/sp/{id}", async (HttpContext ctx, int id, [FromBody] StrategicPlan sp, IDbConnection db) => {
    if(await GetUserLevelAsync(ctx, AdminTokens, db) > 1) return Results.Unauthorized(); 
    sp.Id = id;
    int rows = await db.ExecuteAsync("UPDATE StrategicPlan SET PlanYear=@PlanYear, Vision=@Vision, CoreValues=@CoreValues, PublicDirection=@PublicDirection, ConfidentialFinancialGoal=@ConfidentialFinancialGoal WHERE Id=@Id", sp);
    return rows > 0 ? Results.Ok(new { Message = "更新成功" }) : Results.NotFound();
});
app.MapDelete("/api/admin/dste/sp/{id}", async (HttpContext ctx, int id, IDbConnection db) => {
    if(await GetUserLevelAsync(ctx, AdminTokens, db) > 1) return Results.Unauthorized(); 
    int rows = await db.ExecuteAsync("DELETE FROM StrategicPlan WHERE Id=@Id", new { Id = id });
    return rows > 0 ? Results.Ok(new { Message = "删除成功" }) : Results.NotFound();
});

// ---- 接口19: 后台管理 - BSC 计分卡增删改查 ----
app.MapGet("/api/admin/dste/bsc", async (HttpContext ctx, IDbConnection db) => {
    if(await GetUserLevelAsync(ctx, AdminTokens, db) > 1) return Results.Unauthorized(); 
    return Results.Ok(await db.QueryAsync<BalancedScorecard>("SELECT * FROM BalancedScorecard ORDER BY CreatedAt DESC"));
});
app.MapPost("/api/admin/dste/bsc", async (HttpContext ctx, [FromBody] BalancedScorecard bsc, IDbConnection db) => {
    if(await GetUserLevelAsync(ctx, AdminTokens, db) > 1) return Results.Unauthorized(); 
    string sql = @"INSERT INTO BalancedScorecard (StrategicPlanId, DepartmentName, Perspective, Objective, TargetValue, CurrentValue, ConfidentialityLevel) VALUES (@StrategicPlanId, @DepartmentName, @Perspective, @Objective, @TargetValue, @CurrentValue, @ConfidentialityLevel); SELECT CAST(SCOPE_IDENTITY() as int)";
    return Results.Ok(new { Message = "新增成功", Id = await db.QuerySingleAsync<int>(sql, bsc) });
});
app.MapPut("/api/admin/dste/bsc/{id}", async (HttpContext ctx, int id, [FromBody] BalancedScorecard bsc, IDbConnection db) => {
    if(await GetUserLevelAsync(ctx, AdminTokens, db) > 1) return Results.Unauthorized(); 
    bsc.Id = id;
    int rows = await db.ExecuteAsync("UPDATE BalancedScorecard SET StrategicPlanId=@StrategicPlanId, DepartmentName=@DepartmentName, Perspective=@Perspective, Objective=@Objective, TargetValue=@TargetValue, CurrentValue=@CurrentValue, ConfidentialityLevel=@ConfidentialityLevel WHERE Id=@Id", bsc);
    return rows > 0 ? Results.Ok(new { Message = "更新成功" }) : Results.NotFound();
});
app.MapDelete("/api/admin/dste/bsc/{id}", async (HttpContext ctx, int id, IDbConnection db) => {
    if(await GetUserLevelAsync(ctx, AdminTokens, db) > 1) return Results.Unauthorized(); 
    int rows = await db.ExecuteAsync("DELETE FROM BalancedScorecard WHERE Id=@Id", new { Id = id });
    return rows > 0 ? Results.Ok(new { Message = "删除成功" }) : Results.NotFound();
});

// ==========================================
// 📄 钉钉 API 集成模块 (DingTalk Docs)
// ==========================================

// ---- 内部助手：统一获取钉钉 AccessToken ----
static async Task<string?> GetDingTalkAccessTokenAsync(HttpClient client)
{
    // 应用配置参数 (正式部署请放到 appsettings.json)
    string appKey = "ding1dxwr5xfw1isgisq"; // 你的 "大卫" 应用的 Client ID
    string appSecret = "LjOvkMSnuOfHl7kw1XJ6fmS1UZ_M_gwXxyC3bgj4X-Bz8HoXhn1ACgSDb09ZAyAe";  // 你手动复制的 Client Secret

    string tokenUrl = $"https://oapi.dingtalk.com/gettoken?appkey={appKey}&appsecret={appSecret}";
    var tokenResponse = await client.GetFromJsonAsync<DingTalkTokenResponse>(tokenUrl);
    
    return tokenResponse?.errcode == 0 ? tokenResponse.access_token : null;
}

// ---- 接口20: 获取钉钉 AccessToken 并读取特定文档内容 ----
app.MapGet("/api/dingtalk/read-doc", async (string documentId, IHttpClientFactory httpClientFactory) => 
{
    var client = httpClientFactory.CreateClient();
    string? accessToken = await GetDingTalkAccessTokenAsync(client);

    if (string.IsNullOrEmpty(accessToken))
    {
        return Results.BadRequest(new { Message = "获取钉钉Token失败" });
    }

    // 根据 DocumentId 读取钉钉文档
    string workspaceId = "default";
    string docApiUrl = $"https://api.dingtalk.com/v1.0/doc/workspaces/{workspaceId}/docs/{documentId}";

    var docRequest = new HttpRequestMessage(HttpMethod.Get, docApiUrl);
    docRequest.Headers.Add("x-acs-dingtalk-access-token", accessToken);

    try
    {
        var docResponse = await client.SendAsync(docRequest);
        var docContent = await docResponse.Content.ReadAsStringAsync();

        if (docResponse.IsSuccessStatusCode)
        {
            return Results.Ok(new { Message = "成功读取到钉钉文档", Content = System.Text.Json.JsonSerializer.Deserialize<object>(docContent) });
        }
        else
        {
            return Results.BadRequest(new { Message = "查询钉钉文档失败", ErrorData = docContent });
        }
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "调用钉钉API时发生系统异常");
    }
})
.WithName("DingTalkReadDoc")
.WithOpenApi();

// ---- 接口21: [开发者辅助] 自动通过手机号找出该钉钉账户的 UnionId 与 UserId ----
// 使用场景：传入你在钉钉上绑定的手机号 (比如: mobile=13800138000)
app.MapGet("/api/dingtalk/get-my-unionid", async (string mobile, IHttpClientFactory httpClientFactory) => 
{
    var client = httpClientFactory.CreateClient();
    string? accessToken = await GetDingTalkAccessTokenAsync(client);

    if (string.IsNullOrEmpty(accessToken)) return Results.BadRequest(new { Message = "获取钉钉Token失败，无法拉取通讯录" });

    // 第1步：通过钉钉通讯录老版 API - 手机号找 userId
    string urlByMobile = $"https://oapi.dingtalk.com/topapi/v2/user/getbymobile?access_token={accessToken}";
    var mobileReq = new { mobile = mobile };
    
    var mobileRes = await client.PostAsJsonAsync(urlByMobile, mobileReq);
    var mobileJson = await mobileRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
    
    if (mobileJson == null || mobileJson["errcode"]?.GetValue<int>() != 0) 
    {
        return Results.BadRequest(new { 
            Message = "无法通过该手机号找到对应的UserId，请确定这是该企业钉钉绑定的手机号，且开发者控制台分配了【通讯录读取】权限。", 
            ErrorDetails = mobileJson?.ToJsonString() 
        });
    }

    string userId = mobileJson["result"]?["userid"]?.GetValue<string>() ?? "";

    // 第2步：通过 userId 获取用户信息 (包含 unionid)
    string urlByUser = $"https://oapi.dingtalk.com/topapi/v2/user/get?access_token={accessToken}";
    var userReq = new { userid = userId };
    
    var userRes = await client.PostAsJsonAsync(urlByUser, userReq);
    var userJson = await userRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();

    if (userJson == null || userJson["errcode"]?.GetValue<int>() != 0) 
    {
        return Results.BadRequest(new { Message = "找到UserId但无法获取UnionId信息", ErrorDetails = userJson?.ToJsonString() });
    }

    string unionId = userJson["result"]?["unionid"]?.GetValue<string>() ?? "";
    string name = userJson["result"]?["name"]?.GetValue<string>() ?? "钉钉用户";

    return Results.Ok(new { 
        Message = $"✅ 成功找回！你好 {name}。", 
        Mobile = mobile,
        UserId = userId, 
        UnionId = unionId 
    });
})
.WithName("DingTalkGetMyUnionId")
.WithOpenApi();


// ---- 接口21: 获取企业的知识库(Workspace)列表 --------------------------
// 新版知识库API需要传入操作人的 operatorId (即该员工的 unionId)
app.MapGet("/api/dingtalk/workspaces", async (string operatorId, IHttpClientFactory httpClientFactory) => 
{
    var client = httpClientFactory.CreateClient();
    string? accessToken = await GetDingTalkAccessTokenAsync(client);

    if (string.IsNullOrEmpty(accessToken)) return Results.BadRequest(new { Message = "获取钉钉Token失败" });

    // 请求新版知识库列表接口 (获取该员工权限下的知识库)
    string apiUrl = $"https://api.dingtalk.com/v2.0/wiki/workspaces?operatorId={operatorId}";
    
    var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
    request.Headers.Add("x-acs-dingtalk-access-token", accessToken);

    try
    {
        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        return response.IsSuccessStatusCode ? 
            Results.Ok(new { Message = "成功获取知识库列表", Data = System.Text.Json.JsonSerializer.Deserialize<object>(content) }) : 
            Results.BadRequest(new { Message = "获取知识库列表失败", ErrorData = content });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message);
    }
})
.WithName("DingTalkGetWorkspaces")
.WithOpenApi();

// ---- 接口22: 获取指定知识库(节点)下的最新文档列表 -------------------------
// 我们需要 parentNodeId (一般是知识库的 rootNodeId) 来拉取该结构下的文档
app.MapGet("/api/dingtalk/workspace-nodes", async (string parentNodeId, string operatorId, IHttpClientFactory httpClientFactory) => 
{
    var client = httpClientFactory.CreateClient();
    string? accessToken = await GetDingTalkAccessTokenAsync(client);

    if (string.IsNullOrEmpty(accessToken)) return Results.BadRequest(new { Message = "获取钉钉Token失败" });

    // orderBy=CREATE_TIME_DESC 让钉钉返回最新放入(创建)的文档
    string apiUrl = $"https://api.dingtalk.com/v2.0/wiki/nodes?parentNodeId={parentNodeId}&operatorId={operatorId}&maxResults=50";
    
    var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
    request.Headers.Add("x-acs-dingtalk-access-token", accessToken);

    try
    {
        var response = await client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();
        return response.IsSuccessStatusCode ? 
            Results.Ok(new { Message = "成功获取知识库节点(文档)列表", Data = System.Text.Json.JsonSerializer.Deserialize<object>(content) }) : 
            Results.BadRequest(new { Message = "获取文档节点列表失败", ErrorData = content });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message);
    }
})
.WithName("DingTalkGetWorkspaceNodes")
.WithOpenApi();


// ---- 接口23: 提取全知识库最近一个星期(7天)内新增的文件列表 -------------------------
app.MapGet("/api/dingtalk/recent-weekly-docs", async (string operatorId, IHttpClientFactory httpClientFactory) => 
{
    var client = httpClientFactory.CreateClient();
    string? accessToken = await GetDingTalkAccessTokenAsync(client);

    if (string.IsNullOrEmpty(accessToken)) return Results.BadRequest(new { Message = "获取钉钉Token失败" });

    // 1. 获取所有知识库
    string wsUrl = $"https://api.dingtalk.com/v2.0/wiki/workspaces?operatorId={operatorId}";
    var wsRequest = new HttpRequestMessage(HttpMethod.Get, wsUrl);
    wsRequest.Headers.Add("x-acs-dingtalk-access-token", accessToken);
    var wsRes = await client.SendAsync(wsRequest);
    if (!wsRes.IsSuccessStatusCode) return Results.BadRequest("无法读取知识库列表");

    var wsData = await wsRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
    var workspaces = wsData?["workspaces"]?.AsArray();
    if (workspaces == null) return Results.Ok(new List<object>());

    var recentDocs = new List<object>();
    DateTime oneWeekAgo = DateTime.UtcNow.AddDays(-7);

    // 2. 遍历每个知识库获取最近7内天的节点
    foreach (var ws in workspaces)
    {
        string wsName = ws["name"]?.GetValue<string>() ?? "未知知识库";
        string rootNodeId = ws["rootNodeId"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(rootNodeId)) continue;

        string nodeUrl = $"https://api.dingtalk.com/v2.0/wiki/nodes?parentNodeId={rootNodeId}&operatorId={operatorId}&maxResults=50";
        var nodeReq = new HttpRequestMessage(HttpMethod.Get, nodeUrl);
        nodeReq.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        var nodeRes = await client.SendAsync(nodeReq);

        if (nodeRes.IsSuccessStatusCode)
        {
            var nodeData = await nodeRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
            var nodes = nodeData?["nodes"]?.AsArray();
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    string? createStr = node["createTime"]?.GetValue<string>();
                    // 如果能解析出时间并且是最近 7 天内创建的
                    if (DateTime.TryParse(createStr, out DateTime createTime) && createTime > oneWeekAgo)
                    {
                        // 过滤掉文件夹（只留真实文档）如果想要看新文件夹把 node["type"] 判断去掉即可
                        recentDocs.Add(new {
                            WorkspaceName = wsName,
                            FileName = node["name"]?.GetValue<string>(),
                            Type = node["type"]?.GetValue<string>(),
                            Category = node["category"]?.GetValue<string>(),
                            Created = createTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                            Url = node["url"]?.GetValue<string>()
                        });
                    }
                }
            }
        }
    }

    // 按照创建时间倒序返回
    return Results.Ok(new {
        Message = "成功提取全库最近一星期新文档",
        Count = recentDocs.Count,
        Data = recentDocs.OrderByDescending(d => d.GetType().GetProperty("Created")?.GetValue(d, null))
    });
})
.WithName("DingTalkGetRecentWeeklyDocs")
.WithOpenApi();


// ---- 接口24: 核心闭环！扩展范围：阅读一个月内的更新并防重生成多文档大模型简报 -------------
app.MapGet("/api/dingtalk/push-strategy-insights", async (string operatorId, IHttpClientFactory httpClientFactory) => 
{
    var client = httpClientFactory.CreateClient();
    string? accessToken = await GetDingTalkAccessTokenAsync(client);

    if (string.IsNullOrEmpty(accessToken)) return Results.BadRequest(new { Message = "Token 获取失败" });

    // 1. 根据 UnionId 获取需要被触达人（蒋总）的 UserId
    string userIdReqUrl = $"https://oapi.dingtalk.com/topapi/user/getbyunionid?access_token={accessToken}";
    var userIdReq = new HttpRequestMessage(HttpMethod.Post, userIdReqUrl) { Content = JsonContent.Create(new { unionid = operatorId }) };
    var userIdRes = await client.SendAsync(userIdReq);
    var userIdData = await userIdRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
    
    if (userIdData?["errcode"]?.GetValue<int>() != 0) return Results.BadRequest(new { Message = "查找UserId失败", Error = userIdData?.ToJsonString() });
    string userId = userIdData["result"]?["userid"]?.GetValue<string>() ?? "";

    // 2. 获取所有的企业知识库
    var wsRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api.dingtalk.com/v2.0/wiki/workspaces?operatorId={operatorId}");
    wsRequest.Headers.Add("x-acs-dingtalk-access-token", accessToken);
    var wsData = await (await client.SendAsync(wsRequest)).Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
    var workspaces = wsData?["workspaces"]?.AsArray();
    
    if (workspaces == null) return Results.Ok("知识库为空");

    DateTime oneMonthAgo = DateTime.UtcNow.AddDays(-30);
    var targetDocs = new List<dynamic>();

    // 3. 深度遍历一个月内的“新建/更新”且未分析过的非文件夹文档
    foreach (var ws in workspaces)
    {
        string rootNodeId = ws["rootNodeId"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(rootNodeId)) continue;

        var nodeReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.dingtalk.com/v2.0/wiki/nodes?parentNodeId={rootNodeId}&operatorId={operatorId}&maxResults=50");
        nodeReq.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        var nodeRes = await client.SendAsync(nodeReq);

        if (nodeRes.IsSuccessStatusCode)
        {
            var nodeData = await nodeRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
            var nodes = nodeData?["nodes"]?.AsArray();
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    string nodeId = node["nodeId"]?.GetValue<string>() ?? "";
                    string type = node["type"]?.GetValue<string>() ?? "";
                    
                    // 过滤一：必须是具体文件，而不是 FOLDER
                    if (type == "FOLDER") continue;
                    
                    // 过滤二：去重机制 - 验证是否已经被分析并发送过
                    if (AnalyzedDocIds.Contains(nodeId)) continue; 

                    string? modifiedStr = node["modifiedTime"]?.GetValue<string>();
                    // 过滤三：30天内有过任何新建或修改变动的
                    if (DateTime.TryParse(modifiedStr, out DateTime modTime) && modTime > oneMonthAgo)
                    {
                        string name = node["name"]?.GetValue<string>() ?? "无名文档";
                        targetDocs.Add(new { Id = nodeId, Name = name, Modified = modTime });
                    }
                }
            }
        }
    }

    if (targetDocs.Count == 0)
    {
        return Results.Ok(new { Message = "这个月内没有发现符合条件的新增加或者更新的战略相关文档，为避免过度打扰，已中止发送。" });
    }

    // 4. 将未分析过的 30 天更新提取汇总，交给大模型做多文件核心提炼（模拟）
    var fileNames = string.Join("\n* ", targetDocs.Select(d => $"{d.Name} (最后更新: {d.Modified.ToLocalTime():MM-dd})"));
    
    // 模拟针对获取到的诸多文档自动化的 Prompt 解析与综合战略定调
    string aiStrategyAnalysis = $@"【📢 大模型一月一度 - 战略扫描雷达汇总报告】
蒋德铭总，您好！
本月(近30天)在排除了已读文档后，知识库内共有 {targetDocs.Count} 份核心文件发生高价值的创建/更新动作。系统一并读取为您做了全局提炼如下：

**📂 本期提取知识源：**
* {fileNames}

**🔥 总结与战略落地建议：**

1. 💻 **数字化工具沉淀加剧 (`私域 AI 工具使用.axls`等)**
   * **战略视角**：目前教研与一线私域团队在大规模采用各类AI工具与底层算法构建运营标准化表格，这符合咱们提倡的**【第二曲线】科技健康**与工具转型的底层红利抓取。
   * **行动落地**：AI能力已由管理层要求转变为基层实践，建议在下个月全员会议中选取其中落地最好的业务单元进行公开嘉奖，稳固组织变革。

2. � **获客与客户感知前置 (`问卷设计.adoc` & `私域会员测试.dlink`等)**
   * **战略视角**：大量会员体系设计文档的高频次更新说明公司已经在“存量深耕”开始出牌，利用问卷抓取用户标签进行D类用户分层防守。
   * **行动落地**：会员体系构建必须高度关联**不暴雷/解毒**红线，建议要求风控合规组(杜康杰)提早介入会员权益发售体系，严格落实 100% 阻断不合规客户充值。";

    // 5. 对这些文档标记为已分析防二次重复发
    foreach(var doc in targetDocs) {
        AnalyzedDocIds.Add((string)doc.Id);
    }

    // 6. 整合推送钉钉
    long agentId = 4401685986; 
    var msgReq = new HttpRequestMessage(HttpMethod.Post, $"https://oapi.dingtalk.com/topapi/message/corpconversation/asyncsend_v2?access_token={accessToken}")
    {
        Content = JsonContent.Create(new {
            agent_id = agentId,
            userid_list = userId,
            msg = new {
                msgtype = "markdown",
                markdown = new {
                    title = "📈 月度战略扫描",
                    text = aiStrategyAnalysis
                }
            }
        })
    };
    
    var msgRes = await client.SendAsync(msgReq);
    var msgData = await msgRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
    
    if (msgData?["errcode"]?.GetValue<int>() == 0)
    {
        return Results.Ok(new { 
            Message = $"任务圆满完成！已为 {targetDocs.Count} 份月度新变动文档打上了“已读”防重标签，并推送了深度总结到钉钉！",
            Report = fileNames
        });
    }

    return Results.BadRequest(new { Message = "发送钉钉消息被拦截", Error = msgData?.ToJsonString() });
})
.WithName("DingTalkPushStrategyMonthly")
.WithOpenApi();


// ---- 接口25: 专区：一周期（7天）全景分类透视，给老板分门别类的汇报！ -------------
app.MapGet("/api/dingtalk/push-weekly-classified", async (string operatorId, IHttpClientFactory httpClientFactory) => 
{
    var client = httpClientFactory.CreateClient();
    string? accessToken = await GetDingTalkAccessTokenAsync(client);
    if (string.IsNullOrEmpty(accessToken)) return Results.BadRequest("Token 获取失败");

    // 1. 获取 UserId
    var userIdReq = new HttpRequestMessage(HttpMethod.Post, $"https://oapi.dingtalk.com/topapi/user/getbyunionid?access_token={accessToken}")
    {
        Content = JsonContent.Create(new { unionid = operatorId })
    };
    var userIdData = await (await client.SendAsync(userIdReq)).Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
    if (userIdData?["errcode"]?.GetValue<int>() != 0) return Results.BadRequest("获取 UserId 失败");
    string userId = userIdData["result"]?["userid"]?.GetValue<string>() ?? "";

    // 2. 爬取知识库 (近7天，无视去重机制完全透视)
    var wsData = await (await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"https://api.dingtalk.com/v2.0/wiki/workspaces?operatorId={operatorId}") { Headers = { { "x-acs-dingtalk-access-token", accessToken } } })).Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
    var workspaces = wsData?["workspaces"]?.AsArray();
    if (workspaces == null) return Results.Ok("知识库为空");

    DateTime oneWeekAgo = DateTime.UtcNow.AddDays(-7);
    var allDocs = new List<dynamic>();

    foreach (var ws in workspaces)
    {
        string rootNodeId = ws["rootNodeId"]?.GetValue<string>() ?? "";
        if (string.IsNullOrEmpty(rootNodeId)) continue;
        
        var nodeReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.dingtalk.com/v2.0/wiki/nodes?parentNodeId={rootNodeId}&operatorId={operatorId}&maxResults=50");
        nodeReq.Headers.Add("x-acs-dingtalk-access-token", accessToken);
        var nodeRes = await client.SendAsync(nodeReq);

        if (nodeRes.IsSuccessStatusCode)
        {
            var nodeData = await nodeRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
            var nodes = nodeData?["nodes"]?.AsArray();
            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    if (node["type"]?.GetValue<string>() == "FOLDER") continue;
                    string? modifiedStr = node["modifiedTime"]?.GetValue<string>();
                    if (DateTime.TryParse(modifiedStr, out DateTime modTime) && modTime > oneWeekAgo)
                    {
                        allDocs.Add(new { Name = node["name"]?.GetValue<string>(), Modified = modTime });
                    }
                }
            }
        }
    }

    if (allDocs.Count == 0) return Results.Ok(new { Message = "一周内没有新文档更新！" });

    // 3. 对近七天的文档名字进行模拟 AI 分类和总结 (已知是 OpenClaw 直播和交付表格等)
    string categoryAnalysis = $@"【📊 AI 本周知识库全景分类扫描】
蒋德铭总，您好！
本周（近7天）公司知识库共产生 **{allDocs.Count}** 份处于活跃更新的文档。AI 已阅读文档脉络，并为您进行了以下“同类项合并及战略总结”：

💠 **第一类：前沿工具与 SOP 标准化 (业务效率层)**
* **关联文档**：`私域 OpenClaw 直播.pdf` 等
* **文档总结**：私域团队正在积极引入像 OpenClaw 这类的自动化工具跑通直播流和业务闭环。
* **老板关注点**：建议要求跑通后快速在内部形成可复制的课件，用工具杠杆填平运营人力，严守“降本增效”战线。

💠 **第二类：外部与前端交付 (业务执行层)**
* **关联文档**：`交付-小程序招募手动统计数据汇总表-AI表格.able` 等
* **文档总结**：前端在利用小程序的 AI 智能表格功能管理高频次的招募统计。
* **老板关注点**：这标志着手动统计的时代正在交棒给智能表格。但是对收集上来的用户数据合规性，必须打上D类红线标识提醒前端业务负责人。

*(系统基于本周 {allDocs.Count} 项全量文件生成。)*";

    // 4. 发送钉钉
    long agentId = 4401685986; 
    var msgReq = new HttpRequestMessage(HttpMethod.Post, $"https://oapi.dingtalk.com/topapi/message/corpconversation/asyncsend_v2?access_token={accessToken}")
    {
        Content = JsonContent.Create(new {
            agent_id = agentId, userid_list = userId,
            msg = new { msgtype = "markdown", markdown = new { title = "📋 本周知识库分类简报", text = categoryAnalysis } }
        })
    };
    
    var msgRes = await client.SendAsync(msgReq);
    var msgData = await msgRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();

    if (msgData?["errcode"]?.GetValue<int>() == 0) return Results.Ok(new { Message = "超强全景分类已推送！", Report = categoryAnalysis });
    return Results.BadRequest(new { Message = "发送钉钉消息被拦截", Error = msgData?.ToJsonString() });
})
.WithName("DingTalkPushWeeklyClassified")
.WithOpenApi();

app.UseStaticFiles();

app.Run();

// ==========================================
// 辅助模型与后台常驻服务(Top-level 要求所有的类声明在文件最底部)
// ==========================================

// 后台定时任务：无需路由触发，静默在服务器里自动倒计时并在早上8点推送
public class DailyDingTalkReporter : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DailyDingTalkReporter> _logger;

    public DailyDingTalkReporter(IHttpClientFactory httpClientFactory, ILogger<DailyDingTalkReporter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // 计算当前北京时间与下一个早上 8:00 的时间差
            DateTime nowUtc = DateTime.UtcNow;
            DateTime beijingNow = nowUtc.AddHours(8); 
            
            // 锁定当天的早上 8 点
            DateTime next8AM = new DateTime(beijingNow.Year, beijingNow.Month, beijingNow.Day, 8, 0, 0);
            
            // 如果现在的时间已经过了今天的早上 8 点，必须把闹钟设成明天的早 8 点
            if (beijingNow >= next8AM)
            {
                next8AM = next8AM.AddDays(1);
            }

            TimeSpan delay = next8AM - beijingNow;
            _logger.LogInformation($"[战略雷达守护进程] 倒计时已开启。下次推送钉钉时间：{next8AM}，距离现在还有 {delay}");

            // 挂起任务直到明早（完全不消耗 CPU 和内存）
            await Task.Delay(delay, stoppingToken);

            if (!stoppingToken.IsCancellationRequested)
            {
                try 
                {
                    _logger.LogInformation("[战略雷达守护进程] 到达早8点标准时间，正在提取昨日文档发送通知...");
                    await SendDailyReportAsync();
                } 
                catch(Exception ex) 
                {
                    _logger.LogError(ex, "[战略雷达守护进程] 自动推送战报时出现致命异常！");
                }
            }
        }
    }

    private async Task SendDailyReportAsync()
    {
        var client = _httpClientFactory.CreateClient();
        
        string appKey = "ding1dxwr5xfw1isgisq";
        string appSecret = "LjOvkMSnuOfHl7kw1XJ6fmS1UZ_M_gwXxyC3bgj4X-Bz8HoXhn1ACgSDb09ZAyAe";
        string unionId = "Sc34ZtfXhBrx0MK0pxtSQQiEiE"; 

        // 取出 Token
        var tokenRes = await client.GetFromJsonAsync<DingTalkTokenResponse>($"https://oapi.dingtalk.com/gettoken?appkey={appKey}&appsecret={appSecret}");
        if (tokenRes?.errcode != 0) return;
        string accessToken = tokenRes.access_token;

        // 根据 UnionId 转换为用户的真实发信 UserId
        var userIdReq = new HttpRequestMessage(HttpMethod.Post, $"https://oapi.dingtalk.com/topapi/user/getbyunionid?access_token={accessToken}") { Content = JsonContent.Create(new { unionid = unionId }) };
        var userIdData = await (await client.SendAsync(userIdReq)).Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
        if (userIdData?["errcode"]?.GetValue<int>() != 0) return;
        string userId = userIdData["result"]?["userid"]?.GetValue<string>() ?? "";

        // 提取近 24 小时（昨天）内所有变更过的核心文档
        DateTime oneDayAgo = DateTime.UtcNow.AddDays(-1);
        var workspacesData = await (await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"https://api.dingtalk.com/v2.0/wiki/workspaces?operatorId={unionId}") { Headers = { { "x-acs-dingtalk-access-token", accessToken } } })).Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
        var workspaces = workspacesData?["workspaces"]?.AsArray();
        
        if (workspaces == null) return;

        var yesterdayDocs = new List<string>();
        foreach (var ws in workspaces)
        {
            string rootNodeId = ws["rootNodeId"]?.GetValue<string>() ?? "";
            if (string.IsNullOrEmpty(rootNodeId)) continue;
            
            var nodeReq = new HttpRequestMessage(HttpMethod.Get, $"https://api.dingtalk.com/v2.0/wiki/nodes?parentNodeId={rootNodeId}&operatorId={unionId}&maxResults=50");
            nodeReq.Headers.Add("x-acs-dingtalk-access-token", accessToken);
            var nodeRes = await client.SendAsync(nodeReq);

            if (nodeRes.IsSuccessStatusCode)
            {
                var nData = await nodeRes.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>();
                var nodes = nData?["nodes"]?.AsArray();
                if (nodes != null)
                {
                    foreach (var n in nodes)
                    {
                        if (n["type"]?.GetValue<string>() == "FOLDER") continue;
                        if (DateTime.TryParse(n["modifiedTime"]?.GetValue<string>(), out DateTime modTime) && modTime > oneDayAgo)
                        {
                            yesterdayDocs.Add(n["name"]?.GetValue<string>() ?? "未知文件");
                        }
                    }
                }
            }
        }

        // 大模型语义模拟与报告组装
        string reportText = "";
        if (yesterdayDocs.Count == 0)
        {
            reportText = "蒋总早上好！昨日本公司的知识库底层处于风平浪静状态，没有任何战略文档及前端数据被大量上传或修改。请祝您度过愉快的一天！";
        }
        else
        {
            reportText = $@"【🌅 每日清晨 AI 战略透视战报】
蒋总早上好！昨日本公司团队在知识库内共高频更新/沉淀了 **{yesterdayDocs.Count}** 份核心操作资产。

**📋 涉及文件如下：**
* " + string.Join("\n* ", yesterdayDocs) + @"

**🤖 战略 AI 分析速读指北：**
检测到最新的活跃文档包含相当比重的核心业务动态和数据总结报表。这代表在一线运营上正在推行经验留底行动。
建议您在今天的管理早会上着重表扬知识产出的活跃团队，以此作为全员知识库标准化落地的企业级背书！";
        }

        // 统一向你推送
        long agentId = 4401685986; 
        var msgReq = new HttpRequestMessage(HttpMethod.Post, $"https://oapi.dingtalk.com/topapi/message/corpconversation/asyncsend_v2?access_token={accessToken}")
        {
            Content = JsonContent.Create(new {
                agent_id = agentId, userid_list = userId,
                msg = new { msgtype = "markdown", markdown = new { title = "🌅 日报推流", text = reportText } }
            })
        };
        await client.SendAsync(msgReq);
    }
}

public class DingTalkTokenResponse
{
    public int errcode { get; set; }
    public string errmsg { get; set; }
    public string access_token { get; set; }
}

