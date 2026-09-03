using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Data.Entities;

namespace VideoNote.Server.Data;

public sealed class VideoNoteDbContext(DbContextOptions<VideoNoteDbContext> options)
    : DbContext(options)
{
    public DbSet<Provider> Providers => Set<Provider>();

    public DbSet<ModelConfig> ModelConfigs => Set<ModelConfig>();

    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();

    public DbSet<AnalysisTask> AnalysisTasks => Set<AnalysisTask>();

    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var provider = modelBuilder.Entity<Provider>();
        provider.ToTable("Providers");
        provider.HasKey(item => item.Id);
        provider.Property(item => item.Name).HasMaxLength(200).IsRequired();
        provider.HasIndex(item => item.Name).IsUnique();
        provider.Property(item => item.Protocol).HasConversion<string>().HasMaxLength(32);
        provider.Property(item => item.BaseUrl).HasMaxLength(2048).IsRequired();
        provider.Property(item => item.ApiKey).HasMaxLength(4096).IsRequired();
        provider.Property(item => item.TranscriptionModel).HasMaxLength(200);
        provider.HasMany(item => item.Models)
            .WithOne(item => item.Provider)
            .HasForeignKey(item => item.ProviderId)
            .OnDelete(DeleteBehavior.Cascade);

        var modelConfig = modelBuilder.Entity<ModelConfig>();
        modelConfig.ToTable("ModelConfigs");
        modelConfig.HasKey(item => item.Id);
        modelConfig.Property(item => item.ModelId).HasMaxLength(200).IsRequired();
        modelConfig.HasIndex(item => new { item.ProviderId, item.ModelId }).IsUnique();
        modelConfig.ToTable(table => table.HasCheckConstraint(
            "CK_ModelConfigs_ContextWindow_Positive",
            "ContextWindow > 0"));

        var promptTemplate = modelBuilder.Entity<PromptTemplate>();
        promptTemplate.ToTable("PromptTemplates");
        promptTemplate.HasKey(item => item.Id);
        promptTemplate.Property(item => item.Name).HasMaxLength(200).IsRequired();
        promptTemplate.Property(item => item.Content).IsRequired();

        var analysisTask = modelBuilder.Entity<AnalysisTask>();
        analysisTask.ToTable("AnalysisTasks");
        analysisTask.HasKey(item => item.Id);
        analysisTask.Property(item => item.OriginalFileName).HasMaxLength(260).IsRequired();
        analysisTask.Property(item => item.VideoPath).HasMaxLength(2048).IsRequired();
        analysisTask.Property(item => item.Mode).HasConversion<string>().HasMaxLength(32);
        analysisTask.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
        analysisTask.Property(item => item.StageDescription).HasMaxLength(500);
        analysisTask.ToTable(table => table.HasCheckConstraint(
            "CK_AnalysisTasks_ProgressPercent_Range",
            "ProgressPercent >= 0 AND ProgressPercent <= 100"));
        analysisTask.HasOne(item => item.ModelConfig)
            .WithMany(item => item.AnalysisTasks)
            .HasForeignKey(item => item.ModelConfigId)
            .OnDelete(DeleteBehavior.SetNull);
        analysisTask.HasOne(item => item.PromptTemplate)
            .WithMany(item => item.AnalysisTasks)
            .HasForeignKey(item => item.PromptTemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        var conversationMessage = modelBuilder.Entity<ConversationMessage>();
        conversationMessage.ToTable("ConversationMessages");
        conversationMessage.HasKey(item => item.Id);
        conversationMessage.Property(item => item.Role).HasConversion<string>().HasMaxLength(32);
        conversationMessage.Property(item => item.Content).IsRequired();
        conversationMessage.HasOne(item => item.AnalysisTask)
            .WithMany(item => item.Messages)
            .HasForeignKey(item => item.AnalysisTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
