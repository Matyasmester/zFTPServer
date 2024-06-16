using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FTPServer
{
    public class FtpServer
    {
        private TcpListener listener;

        private readonly string LOG_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ftpserverlog.txt");

        private static readonly string SharingFolderName = "ToShare";
        private readonly string SharingFolderPath = Path.Combine(Directory.GetCurrentDirectory(), SharingFolderName);

        private const int defaultPort = 5555;
        public FtpServer(IPAddress IP, int port)
        {
            listener = new TcpListener(IP, port);

            if(!Directory.Exists(SharingFolderPath)) Directory.CreateDirectory(SharingFolderPath);

            Directory.SetCurrentDirectory(SharingFolderPath);
        }

        public FtpServer() : this(IPAddress.Any, defaultPort)
        {

        }

        public void Start()
        {
            listener.Start();
            listener.BeginAcceptTcpClient(HandleTcpConnection, listener);
        }

        public void Stop()
        {
            listener.Stop();
        }

        public void Dispose()
        {
            listener.Dispose();
        }

        private void WriteToLogFile(string content)
        {
            File.AppendAllText(LOG_PATH, content);
        }

        private string ParseCommand(string command)
        {
            string response = "[?] Command not found.";

            string[] split = command.Split(' ');

            string cmd = split[0].ToUpperInvariant();
            string? args = split.Length > 1 ? split[1] : null;

            string currentDirectory = Directory.GetCurrentDirectory();

            switch (cmd)
            {
                case "CWD":
                    response = ChangeWorkingDirectory(args);
                    break;
                case "PWD":
                    response = currentDirectory;
                    break;
                case "DIR":
                case "LIST":
                    if(string.IsNullOrWhiteSpace(args)) response = GetDirectoryListing(currentDirectory);
                    else response = GetDirectoryListing(args);
                    break;
                default:
                    break;
            }

            return response;
        }

        private string ChangeWorkingDirectory(string path)
        {
            string errorResponse = string.Empty;

            if(!IsValidPath(path, ref errorResponse)) return errorResponse;

            string fullPath = FullyQualifyPath(path);
            Directory.SetCurrentDirectory(fullPath);

            return "[+] Directory successfully changed to " + Directory.GetCurrentDirectory();
        }

        private string FullyQualifyPath(string path)
        {
            if (Path.IsPathFullyQualified(path)) return path;
            else return Path.Combine(Directory.GetCurrentDirectory(), path);
        }

        /*private string Retrieve(string fileName)
        {
            string response = string.Empty;
            string fullPath = FullyQualifyPath(fileName);

            if(!IsValidPath(fullPath, ref response)) return response;


        }*/

        private bool IsValidPath(string path, ref string response)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                response = "[!] No path given.";
                return false;
            }

            if (Path.GetInvalidPathChars().Any(x => path.Contains(x)))
            {
                response = "[!] Invalid char in given path, aborted.";
                return false;
            }

            string fullPath = FullyQualifyPath(path);

            if (!Directory.Exists(fullPath))
            {
                response = "[!] No such directory.";
                return false;
            }

            if (!fullPath.StartsWith(SharingFolderPath) || fullPath.EndsWith(Path.Combine(SharingFolderName, "..")))
            {
                response = "[!] Path outside sharing folder, aborted.";
                return false;
            }

            return true;
        }

        private string GetDirectoryListing(string path)
        {
            string retval = string.Empty;

            if (!IsValidPath(path, ref retval)) return retval; 

            string fullPath = FullyQualifyPath(path);

            string[] paths = Directory.GetFileSystemEntries(fullPath);

            foreach(string directoryEntry in paths)
            {
                string line = string.Empty;
                
                if(Directory.Exists(directoryEntry))
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(directoryEntry);
                    line += dirInfo.CreationTime.ToString() + "\t";
                    line += "<DIR>\t";
                    line += dirInfo.Name + Environment.NewLine;
                }
                else
                {
                    FileInfo fileInfo = new FileInfo(directoryEntry);

                    line += fileInfo.LastWriteTime.ToString() + "\t\t";
                    line += fileInfo.Length + "\t";
                    line += fileInfo.Name + Environment.NewLine;
                }

                retval += line;
            }

            return retval;
        }

        private void HandleTcpConnection(IAsyncResult result)
        {
            TcpClient client = listener.EndAcceptTcpClient(result);

            IPEndPoint? remoteEndPoint = client.Client.RemoteEndPoint as IPEndPoint;

            if (remoteEndPoint == null) return;

            listener.BeginAcceptTcpClient(HandleTcpConnection, listener);

            NetworkStream stream = client.GetStream();

            using StreamWriter writer = new StreamWriter(stream);
            using StreamReader reader = new StreamReader(stream);

            writer.WriteLine("[+] Connected successfully with IP address " + remoteEndPoint.Address + "\r\n");
            writer.Flush();

            string? input = reader.ReadLine();

            while (!string.IsNullOrWhiteSpace(input))
            {
                writer.WriteLine(ParseCommand(input) + "\r\n");
                writer.Flush();

                input = reader.ReadLine();
            }
        }
    }
}
