using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Server.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Login { get; set; } 

        [Required]
        public string Password { get; set; } 

        [Required]
        public string RootDirectory { get; set; } 
        public string CurrentDirectory { get; set; } 
    }
}
