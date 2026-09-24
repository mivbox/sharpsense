using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharpSense.Infrastructure.Persistence.Records;

namespace SharpSense.Infrastructure.Persistence.Configuration;

public sealed class IndexRunStateConfiguration : IEntityTypeConfiguration<IndexRunStateRecord>
{
    public void Configure(EntityTypeBuilder<IndexRunStateRecord> builder)
    {
        builder.ToTable("IndexRunState", table => table.HasCheckConstraint("CK_IndexRunState_Singleton", "Id = 1"));
        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).ValueGeneratedNever();
        builder.Property(state => state.GraphRevision).HasColumnType("TEXT").HasDefaultValue("initial");
        builder.Property(state => state.LastSuccessfulIndexJson).HasColumnType("TEXT");
        builder.Property(state => state.LastAttemptJson).HasColumnType("TEXT");
    }
}
