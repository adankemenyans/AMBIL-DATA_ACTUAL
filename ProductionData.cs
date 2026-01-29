namespace CollectDataAudio
{
    public class ProductionData
    {
        public DateTime DateTime { get; set; } 
        public string Model { get; set; }      
        public int DailyPlan { get; set; }  
        public int Target { get; set; }    
        public int Actual { get; set; } 
        public decimal? Weight { get; set; }
        public decimal? Efficiency { get; set; }
        public string SerialNumber { get; set; }
    }

    public class LineConfig
    {
        public string Name { get; set; }
        public string Ip { get; set; }
        public string TableName { get; set; }
    }
}