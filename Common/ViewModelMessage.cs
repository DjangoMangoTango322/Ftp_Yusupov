namespace Common
{
    public class ViewModelMessage
    {
        public string TypeMessage { get; set; }
        public string Message { get; set; }

        public ViewModelMessage()
        {
            TypeMessage = "";
            Message = "";
        }

        public ViewModelMessage(string typeMessage, string message)
        {
            TypeMessage = typeMessage;
            Message = message;
        }
    }
}
