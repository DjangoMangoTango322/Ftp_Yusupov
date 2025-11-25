using Microsoft.EntityFrameworkCore;

namespace Server.Models
{
    public class AppDbContext : DbContext
    {
        public DbSet<User> Users => Set<User>();

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            string connectionString = "Server=localhost;Database=ftp_server;User=root;Password=;";
            options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
        }
    }
}
