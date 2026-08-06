using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Scoutix.Models;

// Kept in a separate partial class so scaffold-dbcontext can safely overwrite
// ApplicationDbContext.cs without losing DataProtection key storage.
public partial class ApplicationDbContext : IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }
}
