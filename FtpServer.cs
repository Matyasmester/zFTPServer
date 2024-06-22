using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace FTPServer
{
    public class FtpServer : IDisposable
    {
        private TcpListener listener;
        private TcpListener? dataListener;

        private TcpClient? dataClient;

        private readonly string LOG_PATH = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ftpserverlog.txt");

        private static readonly string SharingFolderName = "ToShare";
        private readonly string SharingFolderPath = Path.Combine(Directory.GetCurrentDirectory(), SharingFolderName);

        private const int defaultPort = 5555;
        private int dataPort;

        public FtpServer(IPAddress IP, int port, int dataPort)
        {
            listener = new TcpListener(IP, port);

            this.dataPort = dataPort;

            if(!Directory.Exists(SharingFolderPath)) Directory.CreateDirectory(SharingFolderPath);

            Directory.SetCurrentDirectory(SharingFolderPath);
        }

        public FtpServer() : this(IPAddress.Any, defaultPort, defaultPort + 1)
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
            dataListener?.Dispose();
            dataClient?.Dispose();
        }

        private void WriteToLogFile(string content)
        {
            File.AppendAllText(LOG_PATH, content);
        }

        private string ParseCommand(string command)
        {
            string response = "[?] Command not found.";

            string[] split = command.Split(' ', 2);

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
                case "RETR":
                case "GET":
                    response = Retrieve(args).Result;
                    break;
                case "UPLOAD":
                    response = ReceiveAndSaveFile(args).Result;
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

        private async Task<string> Retrieve(string fileName)
        {
            string response = string.Empty;
            string fullPath = FullyQualifyPath(fileName);

            if(!IsValidPath(fullPath, ref response)) return response;

            while (dataClient == null) await Task.Delay(40);

            NetworkStream dataStream = dataClient.GetStream();

            if (Directory.Exists(fullPath))
            {
                DirectoryInfo dirInfo = new DirectoryInfo(fullPath);
                response = "Later lol";
            }
            else
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(fullPath);

                    byte[] bytes = EncodeSingleFile(fileInfo);

                    SendBytes(bytes, dataStream);

                    response = "[+] Successfully retrieved file " + fileName;
                }
                catch (Exception) 
                {
                    response = "[!] Error retrieving file " + fileName;
                }
            }

            return response;
        }

        private void SendBytes(byte[] bytes, NetworkStream dataStream)
        {
            int bufferSize = 1024;

            int dataLength = bytes.Length;

            byte[] dataLengthBytes = BitConverter.GetBytes(dataLength);

            dataStream.Write(dataLengthBytes, 0, dataLengthBytes.Length);

            int bSent = 0;
            int bLeft = dataLength;

            while (bLeft > 0)
            {
                int currentSize = Math.Min(bLeft, bufferSize);

                dataStream.Write(bytes, bSent, currentSize);

                bSent += currentSize;
                bLeft -= currentSize;
            }
        }

        private async Task<string> ReceiveAndSaveFile(string path)
        {
            int bufferSize = 1024;

            while (dataClient == null) await Task.Delay(40);

            NetworkStream dataStream = dataClient.GetStream();

            byte[] fileSizeBytes = new byte[4];
            dataStream.ReadAsync(fileSizeBytes, 0, 4).Wait();

            int fileSize = BitConverter.ToInt32(fileSizeBytes, 0);

            int bytesLeft = fileSize;
            byte[] fileContent = new byte[fileSize];

            int bytesRead = 0;

            while (bytesLeft > 0)
            {
                int currentSize = Math.Min(bytesLeft, bufferSize);

                if (dataClient.Available < currentSize) currentSize = dataClient.Available;

                await dataStream.ReadAsync(fileContent, bytesRead, currentSize);

                bytesRead += currentSize;
                bytesLeft -= currentSize;
            }

            FileInfo info = new FileInfo(path);
            string name = info.Name;

            try { File.WriteAllBytes(name, fileContent); }
            catch (Exception)
            {
                return "[!] Error saving file " + name;
            }

            return "[+] Succesfully uploaded and saved file " + name;
        }

        private byte[] EncodeSingleFile(FileInfo info)
        {
            using FileStream stream = info.OpenRead();

            byte[] buffer = new byte[stream.Length];

            stream.Read(buffer, 0, buffer.Length);

            return buffer;
        }

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

            if (!Directory.Exists(fullPath) && !File.Exists(fullPath))
            {
                response = "[!] No such file or directory.";
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

        private void HandleDataConnection(IAsyncResult result)
        {
            if (dataListener == null) return;

            dataClient = dataListener.EndAcceptTcpClient(result);

            dataListener.BeginAcceptTcpClient(HandleDataConnection, dataListener);
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

            dataListener = new TcpListener(remoteEndPoint.Address, dataPort);

            dataListener.Start();
            dataListener.BeginAcceptTcpClient(HandleDataConnection, dataListener);

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
