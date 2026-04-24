using Microsoft.EntityFrameworkCore;
using database.Models;

namespace database.Data
{
    public class MetroDbContext : DbContext
    {
        public MetroDbContext(DbContextOptions<MetroDbContext> options) : base(options)
        {
        }

        public DbSet<OperatorInfo> OperatorInfos { get; set; }
        public DbSet<LineInfo> LineInfos { get; set; }
        public DbSet<StationInfo> StationInfos { get; set; }
        public DbSet<SectionInfo> SectionInfos { get; set; }
        public DbSet<TicketTransaction> TicketTransactions { get; set; }
        public DbSet<TrainTimetable> TrainTimetables { get; set; }
        public DbSet<TrainTimetableDetail> TrainTimetableDetails { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<OperatorInfo>(entity =>
            {
                entity.ToTable("operator_info");
                entity.HasKey(e => e.OperatorId);

                entity.Property(e => e.OperatorId)
                      .HasColumnName("operator_id");

                entity.Property(e => e.OperatorCode)
                      .HasColumnName("operator_code")
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.OperatorName)
                      .HasColumnName("operator_name")
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(e => e.ContactPerson)
                      .HasColumnName("contact_person")
                      .HasMaxLength(50);

                entity.Property(e => e.ContactPhone)
                      .HasColumnName("contact_phone")
                      .HasMaxLength(30);

                entity.Property(e => e.CreatedAt)
                      .HasColumnName("created_at");
            });

            modelBuilder.Entity<LineInfo>(entity =>
            {
                entity.ToTable("line_info");
                entity.HasKey(e => e.LineId);

                entity.Property(e => e.LineId)
                      .HasColumnName("line_id");

                entity.Property(e => e.LineCode)
                      .HasColumnName("line_code")
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.LineName)
                      .HasColumnName("line_name")
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(e => e.OperatorId)
                      .HasColumnName("operator_id");

                entity.Property(e => e.CreatedAt)
                      .HasColumnName("created_at");

                entity.HasOne(e => e.Operator)
                      .WithMany()
                      .HasForeignKey(e => e.OperatorId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<StationInfo>(entity =>
            {
                entity.ToTable("station_info");
                entity.HasKey(e => e.StationId);

                entity.Property(e => e.StationId)
                      .HasColumnName("station_id");

                entity.Property(e => e.StationCode)
                      .HasColumnName("station_code")
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.StationName)
                      .HasColumnName("station_name")
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(e => e.IsTransfer)
                      .HasColumnName("is_transfer");

                entity.Property(e => e.CreatedAt)
                      .HasColumnName("created_at");
            });

            modelBuilder.Entity<SectionInfo>(entity =>
            {
                entity.ToTable("section_info");
                entity.HasKey(e => e.SectionId);

                entity.Property(e => e.SectionId)
                      .HasColumnName("section_id");

                entity.Property(e => e.LineId)
                      .HasColumnName("line_id");

                entity.Property(e => e.FromStationId)
                      .HasColumnName("from_station_id");

                entity.Property(e => e.ToStationId)
                      .HasColumnName("to_station_id");

                entity.Property(e => e.DistanceKm)
                      .HasColumnName("distance_km")
                      .HasColumnType("decimal(10,2)");

                entity.Property(e => e.IsBidirectional)
                      .HasColumnName("is_bidirectional");

                entity.HasOne(e => e.Line)
                      .WithMany()
                      .HasForeignKey(e => e.LineId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TicketTransaction>(entity =>
            {
                entity.ToTable("ticket_transaction");
                entity.HasKey(e => e.TransactionId);

                entity.Property(e => e.TransactionId)
                      .HasColumnName("transaction_id");

                entity.Property(e => e.CardNo)
                      .HasColumnName("card_no")
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.EntryTime)
                      .HasColumnName("entry_time");

                entity.Property(e => e.EntryStationId)
                      .HasColumnName("entry_station_id");

                entity.Property(e => e.ExitTime)
                      .HasColumnName("exit_time");

                entity.Property(e => e.ExitStationId)
                      .HasColumnName("exit_station_id");

                entity.Property(e => e.PayAmount)
                      .HasColumnName("pay_amount")
                      .HasColumnType("decimal(10,2)");

                entity.Property(e => e.PaymentType)
                      .HasColumnName("payment_type")
                      .HasMaxLength(20);

                entity.Property(e => e.TransactionType)
                      .HasColumnName("transaction_type")
                      .HasMaxLength(20);

                entity.Property(e => e.TransactionStatus)
                      .HasColumnName("transaction_status")
                      .HasMaxLength(20);

                entity.Property(e => e.ExceptionType)
                      .HasColumnName("exception_type")
                      .HasMaxLength(30);

                entity.Property(e => e.CreatedAt)
                      .HasColumnName("created_at");
            });
            modelBuilder.Entity<TrainTimetable>(entity =>
            {
                entity.ToTable("train_timetable");
                entity.HasKey(e => e.TimetableId);

                entity.Property(e => e.TimetableId)
                      .HasColumnName("timetable_id")
                      .ValueGeneratedOnAdd();

                entity.Property(e => e.TimetableCode)
                      .HasColumnName("timetable_code")
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.TimetableName)
                      .HasColumnName("timetable_name")
                      .HasMaxLength(100)
                      .IsRequired();

                entity.Property(e => e.LineId)
                      .HasColumnName("line_id");

                entity.Property(e => e.Direction)
                      .HasColumnName("direction");

                entity.Property(e => e.VersionNo)
                      .HasColumnName("version_no")
                      .HasMaxLength(20)
                      .IsRequired();

                entity.Property(e => e.EffectiveStartDate)
                      .HasColumnName("effective_start_date");

                entity.Property(e => e.EffectiveEndDate)
                      .HasColumnName("effective_end_date");

                entity.Property(e => e.RunCalendarType)
                      .HasColumnName("run_calendar_type")
                      .HasMaxLength(20);

                entity.Property(e => e.Status)
                      .HasColumnName("status");

                entity.Property(e => e.IsActive)
                      .HasColumnName("is_active");

                entity.Property(e => e.Remark)
                      .HasColumnName("remark")
                      .HasMaxLength(255);

                entity.Property(e => e.CreatedAt)
                      .HasColumnName("created_at");

                entity.Property(e => e.UpdatedAt)
                      .HasColumnName("updated_at");

                entity.HasOne(e => e.Line)
                      .WithMany()
                      .HasForeignKey(e => e.LineId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasMany(e => e.Details)
                      .WithOne(d => d.Timetable)
                      .HasForeignKey(d => d.TimetableId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
            modelBuilder.Entity<TrainTimetableDetail>(entity =>
            {
                entity.ToTable("train_timetable_detail");
                entity.HasKey(e => e.DetailId);

                entity.Property(e => e.DetailId)
                      .HasColumnName("detail_id")
                      .ValueGeneratedOnAdd();

                entity.Property(e => e.TimetableId)
                      .HasColumnName("timetable_id");

                entity.Property(e => e.TrainNo)
                      .HasColumnName("train_no")
                      .HasMaxLength(50)
                      .IsRequired();

                entity.Property(e => e.StationId)
                      .HasColumnName("station_id");

                entity.Property(e => e.StationSeq)
                      .HasColumnName("station_seq");

                entity.Property(e => e.ArrivalTime)
                      .HasColumnName("arrival_time");

                entity.Property(e => e.DepartureTime)
                      .HasColumnName("departure_time");

                entity.Property(e => e.StopMinutes)
                      .HasColumnName("stop_minutes");

                entity.Property(e => e.IsOriginStation)
                      .HasColumnName("is_origin_station");

                entity.Property(e => e.IsTerminalStation)
                      .HasColumnName("is_terminal_station");

                entity.Property(e => e.CreatedAt)
                      .HasColumnName("created_at");

                entity.HasOne(e => e.Timetable)
                      .WithMany(t => t.Details)
                      .HasForeignKey(e => e.TimetableId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Station)
                      .WithMany()
                      .HasForeignKey(e => e.StationId)
                      .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}