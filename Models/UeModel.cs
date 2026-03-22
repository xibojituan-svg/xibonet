namespace XiboNetBackend.Models
{
    public class UeRequest
    {
        public string ChannelName { get; set; } = string.Empty;
        
        // 投放总金额
        public decimal MarketingCost { get; set; }
        
        // 获得的线索/线索数
        public int LeadsCount { get; set; }
        
        // 平均客单价
        public decimal AverageOrderValue { get; set; }
        
        // 预期转化率 (0-1)
        public decimal ConversionRate { get; set; }
    }
}
