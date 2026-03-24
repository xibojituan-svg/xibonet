using System;

namespace XiboNetBackend.Models
{
    // ==========================================
    // DSTE 数据传输对象 (DTO) - 用于动态脱敏返回
    // ==========================================

    public class StrategicPlanDto
    {
        public int Id { get; set; }
        public int PlanYear { get; set; }
        public string Vision { get; set; } = string.Empty;
        public string CoreValues { get; set; } = string.Empty;
        public string PublicDirection { get; set; } = string.Empty;
        
        /// <summary>
        /// 核心财务目标。如果是 L1 高管，则返回具体数字字符串(如 "10,000,000")；否则返回 "****** (机密)"
        /// </summary>
        public string FinancialGoalDisplay { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class BalancedScorecardDto
    {
        public int Id { get; set; }
        public int StrategicPlanId { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string Perspective { get; set; } = string.Empty;
        public string Objective { get; set; } = string.Empty;
        
        /// <summary>
        /// 目标值展示。越权访问时，将转为进度条百分比，如 "75%"
        /// </summary>
        public string TargetValueDisplay { get; set; } = string.Empty;
        /// <summary>
        /// 当前值展示。越权访问时可能被隐藏
        /// </summary>
        public string CurrentValueDisplay { get; set; } = string.Empty;

        public int ConfidentialityLevel { get; set; } 
        public DateTime CreatedAt { get; set; }
    }

    public class ObjectiveKeyResultDto
    {
        public int Id { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string Objective { get; set; } = string.Empty;
        public string KeyResults { get; set; } = string.Empty;
        public string Alignment { get; set; } = string.Empty; // 对齐到了哪个战役
        public int ProgressPercent { get; set; } // 假设我们把完成度转换成百分比
        public bool IsFendouzhe { get; set; } // “奋斗者”高光标识
        public DateTime CreatedAt { get; set; }
    }

    public class KeyPerformanceIndicatorDto
    {
        public int Id { get; set; }
        public string OwnerName { get; set; } = string.Empty;
        public string IndicatorName { get; set; } = string.Empty;
        public string TargetScoreDisplay { get; set; } = string.Empty; // 分数脱敏
        public string ActualScoreDisplay { get; set; } = string.Empty;
        public string EvaluationPeriod { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty; // 评级 (A/B/C/D)
        public string Suggestion { get; set; } = string.Empty; // 打分建议
    }
}
