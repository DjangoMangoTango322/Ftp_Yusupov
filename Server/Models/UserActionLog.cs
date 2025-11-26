using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Models
{
    public class UserActionLog
    {
        public int Id { get; set; }
        public int? UserId { get; set; }  
        public User? User { get; set; }

        public string Command { get; set; } = "";
        public string Arguments { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; }
        public string Result { get; set; } = "";
    }
}