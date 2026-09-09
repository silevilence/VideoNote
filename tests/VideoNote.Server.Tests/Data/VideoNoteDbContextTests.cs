using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VideoNote.Server.Data;
using VideoNote.Server.Data.Entities;
using VideoNote.Shared.Domain;

namespace VideoNote.Server.Tests.Data;

public sealed class VideoNoteDbContextTests
{
    [Fact]
    public async Task Provider_and_model_capabilities_round_trip_through_sqlite()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;

        var provider = new Provider
        {
            Name = "Gemini",
            Protocol = ProviderProtocol.GeminiNative,
            BaseUrl = "https://generativelanguage.googleapis.com",
            ApiKey = "secret",
            TranscriptionModel = "gemini-transcribe"
        };
        provider.Models.Add(new ModelConfig
        {
            ModelId = "gemini-2.5-pro",
            ContextWindow = 1_048_576,
            SupportsReasoning = true,
            SupportsToolCalling = true,
            SupportsStreaming = true,
            SupportsImage = true,
            SupportsAudio = true,
            SupportsVideo = true
        });

        context.Providers.Add(provider);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var saved = await context.Providers.Include(item => item.Models).SingleAsync();

        Assert.Equal("Gemini", saved.Name);
        Assert.Equal(ProviderProtocol.GeminiNative, saved.Protocol);
        Assert.Equal("gemini-transcribe", saved.TranscriptionModel);
        var model = Assert.Single(saved.Models);
        Assert.Equal("gemini-2.5-pro", model.ModelId);
        Assert.Equal(1_048_576, model.ContextWindow);
        Assert.True(model.SupportsReasoning);
        Assert.True(model.SupportsToolCalling);
        Assert.True(model.SupportsStreaming);
        Assert.True(model.SupportsImage);
        Assert.True(model.SupportsAudio);
        Assert.True(model.SupportsVideo);

        saved.Name = "Gemini Native";
        model.ContextWindow = 2_000_000;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        saved = await context.Providers.Include(item => item.Models).SingleAsync();
        Assert.Equal("Gemini Native", saved.Name);
        Assert.Equal(2_000_000, Assert.Single(saved.Models).ContextWindow);
    }

    [Fact]
    public async Task Prompt_template_supports_crud_through_sqlite()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;

        var template = new PromptTemplate
        {
            Name = "重点提取",
            Content = "提取视频中的关键结论。",
            IsBuiltIn = true
        };
        context.PromptTemplates.Add(template);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var saved = await context.PromptTemplates.SingleAsync(p => p.Id == template.Id);
        Assert.Equal("提取视频中的关键结论。", saved.Content);

        saved.Content = "提取关键结论并说明依据。";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        saved = await context.PromptTemplates.SingleAsync(p => p.Id == template.Id);
        Assert.Equal("提取关键结论并说明依据。", saved.Content);

        context.PromptTemplates.Remove(saved);
        await context.SaveChangesAsync();

        Assert.False(await context.PromptTemplates.AnyAsync(p => p.Id == template.Id));
    }

    [Fact]
    public async Task Analysis_task_fields_round_trip_through_sqlite()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;

        var provider = new Provider
        {
            Name = "Local Gateway",
            Protocol = ProviderProtocol.OpenAiCompatible,
            BaseUrl = "http://localhost:8080/v1",
            ApiKey = "secret"
        };
        var model = new ModelConfig
        {
            Provider = provider,
            ModelId = "vision-model",
            ContextWindow = 128_000,
            SupportsImage = true
        };
        var template = new PromptTemplate
        {
            Name = "摘要",
            Content = "生成结构化摘要。"
        };
        var task = new AnalysisTask
        {
            OriginalFileName = "demo.mp4",
            VideoPath = "videos/demo.mp4",
            Mode = AnalysisMode.SampledFrames,
            Status = AnalysisTaskStatus.Understanding,
            ProgressPercent = 60,
            StageDescription = "正在理解第 3 段",
            ResultText = "# 初步结果",
            ModelConfig = model,
            PromptTemplate = template
        };

        context.AnalysisTasks.Add(task);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var saved = await context.AnalysisTasks.SingleAsync();

        Assert.Equal("demo.mp4", saved.OriginalFileName);
        Assert.Equal("videos/demo.mp4", saved.VideoPath);
        Assert.Equal(AnalysisMode.SampledFrames, saved.Mode);
        Assert.Equal(AnalysisTaskStatus.Understanding, saved.Status);
        Assert.Equal(60, saved.ProgressPercent);
        Assert.Equal("正在理解第 3 段", saved.StageDescription);
        Assert.Equal("# 初步结果", saved.ResultText);
        Assert.NotNull(saved.ModelConfigId);
        Assert.NotNull(saved.PromptTemplateId);

        saved.Status = AnalysisTaskStatus.Completed;
        saved.ProgressPercent = 100;
        saved.ResultText = "# 最终结果";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        saved = await context.AnalysisTasks.SingleAsync();
        Assert.Equal(AnalysisTaskStatus.Completed, saved.Status);
        Assert.Equal(100, saved.ProgressPercent);
        Assert.Equal("# 最终结果", saved.ResultText);
    }

    [Fact]
    public async Task Conversation_message_supports_crud_and_is_removed_with_its_task()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;

        var task = new AnalysisTask
        {
            OriginalFileName = "conversation.mp4",
            VideoPath = "videos/conversation.mp4"
        };
        task.Messages.Add(new ConversationMessage
        {
            Role = ConversationRole.Assistant,
            Content = "视频主要讨论了测试驱动开发。"
        });

        context.AnalysisTasks.Add(task);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var saved = await context.ConversationMessages.SingleAsync();
        Assert.Equal(ConversationRole.Assistant, saved.Role);
        Assert.Equal("视频主要讨论了测试驱动开发。", saved.Content);

        saved.Content = "视频主要讨论了集成测试。";
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        saved = await context.ConversationMessages.SingleAsync();
        Assert.Equal("视频主要讨论了集成测试。", saved.Content);

        var savedTask = await context.AnalysisTasks.SingleAsync();
        context.AnalysisTasks.Remove(savedTask);
        await context.SaveChangesAsync();

        Assert.Empty(await context.ConversationMessages.ToListAsync());
    }

    [Fact]
    public async Task Removing_configuration_preserves_analysis_task_history()
    {
        await using var database = await TestDatabase.CreateAsync();
        var context = database.Context;

        var provider = new Provider
        {
            Name = "Disposable Provider",
            Protocol = ProviderProtocol.OpenAiCompatible,
            BaseUrl = "http://localhost/v1",
            ApiKey = "secret"
        };
        var model = new ModelConfig
        {
            Provider = provider,
            ModelId = "disposable-model",
            ContextWindow = 32_000
        };
        var template = new PromptTemplate
        {
            Name = "Disposable Template",
            Content = "Generate a report."
        };
        context.AnalysisTasks.Add(new AnalysisTask
        {
            OriginalFileName = "history.mp4",
            VideoPath = "videos/history.mp4",
            ModelConfig = model,
            PromptTemplate = template,
            ResultText = "A report that must remain."
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        context.Providers.Remove(await context.Providers.SingleAsync());
        context.PromptTemplates.Remove(await context.PromptTemplates.SingleAsync(p => p.Id == template.Id));
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var savedTask = await context.AnalysisTasks.SingleAsync();
        Assert.Null(savedTask.ModelConfigId);
        Assert.Null(savedTask.PromptTemplateId);
        Assert.Equal("A report that must remain.", savedTask.ResultText);
        Assert.Empty(await context.Providers.ToListAsync());
        Assert.Empty(await context.ModelConfigs.ToListAsync());
        Assert.False(await context.PromptTemplates.AnyAsync(p => p.Id == template.Id));
    }

    [Fact]
    public async Task Initial_migration_creates_the_core_schema()
    {
        await using var database = await TestDatabase.CreateAsync(ensureCreated: false);
        var context = database.Context;
        await context.Database.MigrateAsync();

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

        Assert.Contains(appliedMigrations, migration => migration.EndsWith("_InitialCreate"));
        Assert.True(await context.Database.CanConnectAsync());
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(SqliteConnection connection, VideoNoteDbContext context)
        {
            Connection = connection;
            Context = context;
        }

        private SqliteConnection Connection { get; }

        public VideoNoteDbContext Context { get; }

        public static async Task<TestDatabase> CreateAsync(bool ensureCreated = true)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<VideoNoteDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new VideoNoteDbContext(options);

            if (ensureCreated)
            {
                await context.Database.EnsureCreatedAsync();
            }

            return new TestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }
}
