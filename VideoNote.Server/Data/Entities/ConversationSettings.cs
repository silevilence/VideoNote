namespace VideoNote.Server.Data.Entities;

public sealed class ConversationSettings
{
    public int Id { get; set; } = 1;
    public Guid? ModelConfigId { get; set; }
    public ModelConfig? ModelConfig { get; set; }
}
