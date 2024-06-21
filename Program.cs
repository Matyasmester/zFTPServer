namespace FTPServer
{
    internal class Program
    {
        static void Main()
        {
            FtpServer server = new FtpServer();

            server.Start();

            Console.ReadKey();

            server.Dispose();
        }
    }
}
