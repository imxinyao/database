namespace database.DTOs
{
    public class MetroNetworkDto
    {
        public List<MetroStationNodeDto> Stations { get; set; } = new();

        public List<MetroSectionEdgeDto> Sections { get; set; } = new();
    }

    public class MetroStationNodeDto
    {
        public long StationId { get; set; }

        public string StationCode { get; set; } = string.Empty;

        public string StationName { get; set; } = string.Empty;

        public bool IsTransfer { get; set; }

        public int X { get; set; }

        public int Y { get; set; }
    }

    public class MetroSectionEdgeDto
    {
        public long SectionId { get; set; }

        public long LineId { get; set; }

        public string LineCode { get; set; } = string.Empty;

        public string LineName { get; set; } = string.Empty;

        public long FromStationId { get; set; }

        public long ToStationId { get; set; }

        public decimal DistanceKm { get; set; }

        public bool IsBidirectional { get; set; }
    }
}