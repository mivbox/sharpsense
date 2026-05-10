using Microsoft.EntityFrameworkCore;

namespace SharpSense.Infrastructure.CommandExecution;

public sealed class TransientExecutionLogDbContext(DbContextOptions<TransientExecutionLogDbContext> options)
    : DbContext(options);
