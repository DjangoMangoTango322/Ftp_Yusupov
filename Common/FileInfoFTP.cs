namespace Common
{
    public class FileInfoFTP
    {
        public string Name { get; set; }
        public byte[] Data { get; set; }

        public FileInfoFTP()
        {
            Name = "";
            Data = new byte[0];
        }

        public FileInfoFTP(byte[] data, string name)
        {
            Data = data;
            Name = name;
        }
    }
}
