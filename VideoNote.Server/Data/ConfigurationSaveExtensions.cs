using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace VideoNote.Server.Data;

public enum ConfigurationConflict { Duplicate, MissingReference }

public static class ConfigurationSaveExtensions
{
    /// <summary>Only expected unique/foreign-key conflicts are recoverable configuration errors.</summary>
    public static async Task<ConfigurationConflict?> SaveConfigurationAsync(this VideoNoteDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 2067 })
        {
            return ConfigurationConflict.Duplicate;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        {
            return ConfigurationConflict.MissingReference;
        }
    }
}
