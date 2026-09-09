using VideoNote.Server.Data.Entities;
namespace VideoNote.Server.Data;

public static class BuiltInPrompts
{
    public static PromptTemplate[] Create()
    {
        var date = new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc);
        return new[]
        {
            ("10000000-0000-0000-0000-000000000001", "大纲提取", "根据视频内容生成层次清晰的大纲。使用 Markdown 标题和列表，按主题或时间顺序组织。仅在物料提供时间信息时标注时间戳；不编造视频未提供的信息。"),
            ("10000000-0000-0000-0000-000000000002", "重点提取", "提取视频的关键观点、结论和支持依据。使用 Markdown 列表，区分事实、观点与不确定内容；仅在物料提供时标注相关时间戳。"),
            ("10000000-0000-0000-0000-000000000003", "摘要", "用中文概述视频主题、主要内容和结论。输出简洁的 Markdown 摘要，保留必要背景和限制，不添加视频未支持的信息。"),
            ("10000000-0000-0000-0000-000000000004", "关键词", "从视频提取核心关键词、术语及简短解释。使用 Markdown 表格，按重要程度排序，解释应以视频内容为依据。")
        }.Select(p => new PromptTemplate { Id = Guid.Parse(p.Item1), Name = p.Item2, Content = p.Item3,
            IsBuiltIn = true, CreatedAtUtc = date, UpdatedAtUtc = date }).ToArray();
    }
}
