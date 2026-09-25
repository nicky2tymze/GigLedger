using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GigLedger.Data;

public static class LedgerDatabase
{
    /// <summary>Opens a context on the connection and brings the schema up to date.</summary>
    public static LedgerContext Open(SqliteConnection connection)
    {
        var db = new LedgerContext(new DbContextOptionsBuilder<LedgerContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
        return db;
    }
}
