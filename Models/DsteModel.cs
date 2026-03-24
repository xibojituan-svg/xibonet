using System;

namespace XiboNetBackend.Models
{
    // ==========================================
    // DSTE 战略管理与执行体系数据模型 (Dapper POCO)
    // ==========================================

    /// <summary>
    /// 战略规划 (SP/BP) - L1决策层与全员愿景
    /// </summary>
    public class StrategicPlan
    {
        public int Id { get; set; }
        public int PlanYear { get; set; }
        /// <summary>
        /// 战略愿景 (全员公开)
        /// </summary>
        public string Vision { get; set; } = string.Empty;
        /// <summary>
        /// 核心价值观 (全员公开)
        /// </summary>
        public string CoreValues { get; set; } = string.Empty;
        /// <summary>
        /// 年度公开战略方向 (脱敏版，L2-L4可见)
        /// </summary>
        public string PublicDirection { get; set; } = string.Empty;
        /// <summary>
        /// 核心财务目标/投资计划 (高度保密，仅L1可见)
        /// </summary>
        public decimal ConfidentialFinancialGoal { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 平衡计分卡 (BSC) - 战略解码到部门
    /// </summary>
    public class BalancedScorecard
    {
        public int Id { get; set; }
        public int StrategicPlanId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        /// <summary>
        /// 视角：财务、客户、内部流程、学习与成长
        /// </summary>
        public string Perspective { get; set; } = string.Empty;
        public string Objective { get; set; } = string.Empty;
        public decimal TargetValue { get; set; }
        public decimal CurrentValue { get; set; }
        /// <summary>
        /// 保密等级：1(L1专属), 2(L1/L2可见), 3(向下公开)
        /// </summary>
        public int ConfidentialityLevel { get; set; } 
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 关键战役 (GSA) - 部门级必赢之战
    /// </summary>
    public class GoalStrategyAction
    {
        public int Id { get; set; }
        public int BscId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string GoalName { get; set; } = string.Empty;
        public string StrategyDescription { get; set; } = string.Empty;
        public string ActionPlan { get; set; } = string.Empty;
        /// <summary>
        /// 是否跨部门战役 (True则对被协同部门透明)
        /// </summary>
        public bool IsCrossDepartment { get; set; }
        /// <summary>
        /// 状态：未开始、进行中、已完成、已复盘
        /// </summary>
        public string Status { get; set; } = "未开始";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 目标与关键成果 (OKR) - 个人/团队创新对齐
    /// </summary>
    public class ObjectiveKeyResult
    {
        public int Id { get; set; }
        /// <summary>
        /// 关联的战役ID，确保底层工作能对齐到战略GSA
        /// </summary>
        public int? GsaId { get; set; }
        public int OwnerId { get; set; } // 关联 Users 表
        public string Objective { get; set; } = string.Empty;
        public string KeyResults { get; set; } = string.Empty; // 简易存为JSON或文本列表
        /// <summary>
        /// 是否私密 (默认 false，即鼓励全员透明寻协同)
        /// </summary>
        public bool IsPrivate { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 关键绩效指标 (KPI) - 个人算账与考核
    /// </summary>
    public class KeyPerformanceIndicator
    {
        public int Id { get; set; }
        public int OwnerId { get; set; } // 关联 Users 表
        public string IndicatorName { get; set; } = string.Empty;
        public decimal Weight { get; set; } // 权重%
        public decimal TargetScore { get; set; }
        public decimal ActualScore { get; set; }
        public string EvaluationPeriod { get; set; } = string.Empty; // 例：2026-H1
        /// <summary>
        /// 是否保密 (默认 true，仅本人、直属主管和HR可见)
        /// </summary>
        public bool IsConfidential { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}
