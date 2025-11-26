using Microsoft.EntityFrameworkCore;

namespace Server.Models
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<UserActionLog> ActionLogs => Set<UserActionLog>();
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            string connectionString = "Server=127.0.0.1;Database=ftp_server;User=root;Password=;";
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        }
    }
}
