using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Common
{
    public class ViewModelMessage
    {
        public string Command {  get; set; }
        public string Data { get; set; }
        public ViewModelMessage(string command, string data)
        {
            this.Command = command;
            this.Data = data;
        }
    }
}
