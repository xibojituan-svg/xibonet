-- ==========================================
-- Xibo DSTE 战略管理与执行体系数据库表结构梳理
-- 适用于 SQL Server
-- ==========================================

-- 1. 战略规划表 (StrategicPlan)
-- 保密等级：极高的财务指标仅L1可见，公开愿景全员可见。
CREATE TABLE StrategicPlan (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    PlanYear INT NOT NULL,
    Vision NVARCHAR(500) NOT NULL,            -- 战略愿景 (全员公开)
    CoreValues NVARCHAR(1000) NOT NULL,       -- 核心价值观 (全员公开)
    PublicDirection NVARCHAR(1000) NOT NULL,  -- 年度公开战略方向 (对外脱敏，给基层宣讲)
    ConfidentialFinancialGoal DECIMAL(18,2),  -- 核心财务/绝密目标 (仅L1可视)
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- 2. 平衡计分卡表 (BalancedScorecard)
-- 从集团战略解码到各中心的4个维度指标。
CREATE TABLE BalancedScorecard (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    StrategicPlanId INT NOT NULL FOREIGN KEY REFERENCES StrategicPlan(Id),
    DepartmentName NVARCHAR(100) NOT NULL,
    Perspective NVARCHAR(50) NOT NULL,        -- 视角：财务/客户/内部流程/学习与成长
    Objective NVARCHAR(200) NOT NULL,         -- 目标名称
    TargetValue DECIMAL(18,2) NOT NULL,       -- 目标值 (对于下级将自动转换为达成率脱敏)
    CurrentValue DECIMAL(18,2) NOT NULL,      -- 当前值
    ConfidentialityLevel INT DEFAULT 3,       -- 保密层级: 1(L1专属), 2(L1/L2可见), 3(公开)
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- 3. 关键战役表 (GoalStrategyAction)
-- 部门级必须打赢的战役，牵引底层OKR。
CREATE TABLE GoalStrategyAction (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    BscId INT NOT NULL FOREIGN KEY REFERENCES BalancedScorecard(Id),
    DepartmentName NVARCHAR(100) NOT NULL,
    GoalName NVARCHAR(200) NOT NULL,          -- 战役目标 (Goal)
    StrategyDescription NVARCHAR(1000),       -- 战役策略 (Strategy)
    ActionPlan NVARCHAR(MAX),                 -- 行动计划 (Action)
    IsCrossDepartment BIT DEFAULT 0,          -- 是否跨部门战役 (True则对被协同部门透明)
    Status NVARCHAR(50) DEFAULT '未开始',     -- 状态
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- 4. 目标与关键成果表 (ObjectiveKeyResult)
-- 员工/团队根据战役分解的协同创新目标，默认全员透明。
CREATE TABLE ObjectiveKeyResult (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    GsaId INT NULL FOREIGN KEY REFERENCES GoalStrategyAction(Id), -- 对齐到战役
    OwnerId INT NOT NULL FOREIGN KEY REFERENCES Users(Id),        -- 关联负责人 (前提:已有Users表)
    Objective NVARCHAR(200) NOT NULL,         -- O: 业务目标
    KeyResults NVARCHAR(1000) NOT NULL,       -- KR: 关键结果 (JSON或文本记录)
    IsPrivate BIT DEFAULT 0,                  -- 是否私密 (多数默认为False，追求对齐透明)
    CreatedAt DATETIME DEFAULT GETDATE()
);

-- 5. 关键绩效指标表 (KeyPerformanceIndicator)
-- 员工底线考核和算账指标，与薪酬挂钩，严格保密。
CREATE TABLE KeyPerformanceIndicator (
    Id INT IDENTITY(1,1) PRIMARY KEY,
    OwnerId INT NOT NULL FOREIGN KEY REFERENCES Users(Id),        -- 关联被考核人
    IndicatorName NVARCHAR(200) NOT NULL,     -- 考核项
    Weight DECIMAL(5,2) NOT NULL,             -- 权重(%)
    TargetScore DECIMAL(18,2) NOT NULL,       -- 目标分数
    ActualScore DECIMAL(18,2) DEFAULT 0,      -- 实际得分
    EvaluationPeriod NVARCHAR(50) NOT NULL,   -- 考核周期 e.g. '2026-H1'
    IsConfidential BIT DEFAULT 1,             -- 是否保密 (考核数据默认高度保密)
    CreatedAt DATETIME DEFAULT GETDATE()
);
