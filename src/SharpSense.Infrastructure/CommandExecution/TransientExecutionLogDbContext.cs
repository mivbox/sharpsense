using Microsoft.EntityFrameworkCore;

namespace SharpSense.Infrastructure.CommandExecution;

internal sealed class TransientExecutionLogDbContext(DbContextOptions<TransientExecutionLogDbContext> options)
    : DbContext(options);
