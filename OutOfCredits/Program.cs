using SMBLibrary;
using SMBLibrary.Client;

using System.Diagnostics.CodeAnalysis;
using System.Net;

using FileAttributes = SMBLibrary.FileAttributes;

namespace OutOfCredits;

internal class Program
{
    private static ISMBClient _smbClient = new SMB2Client();
    private static ISMBFileStore? _fileStore;

    private static PeriodicTimer _periodicTimer = new(TimeSpan.FromSeconds(5));

    private static Settings _settings = new(
        Domain: "Domain",
        Login: "Username",
        Password: "Password");

    private static SmbPath _smbPath = new(
        ServerName: "Server",
        ShareName: "Share");

    record Settings(string Domain, string Login, string Password);

    record SmbPath(string ServerName, string ShareName);

    static async Task Mains(string[] args)
    {
        Connect();

        const int payloadLength = 15 * 1024 * 1024; // 15 MB
        byte[] payload = new byte[payloadLength];
        Random.Shared.NextBytes(payload.AsSpan());
        var payloadStream = new MemoryStream(payload, false);
        var counter = 0;

        while (true)
        {
            payloadStream.Position = 0;
            Console.WriteLine($"Current available credits: {_smbClient.GetAvailableCredits()}");
            try
            {
                var relativePath = $"file-{counter++}";
                Send(payloadStream, relativePath);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            Console.WriteLine("Wait for next tick");
            await _periodicTimer.WaitForNextTickAsync();
        }

    }

    private static void Send(Stream stream, string relativePath)
    {
        const int DefaultBufferSize = 1024 * 80; // 80 KiB to avoid LOH (85KB)

        var ntStatus = _fileStore.CreateFile(
            out var fileHandle,
            out _,
            relativePath,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
            FileAttributes.Normal,
            ShareAccess.None,
            CreateDisposition.FILE_SUPERSEDE,
            CreateOptions.FILE_NON_DIRECTORY_FILE,
            null);

        WriteIfInvalidSmbStatus(ntStatus);
        if (ntStatus != NTStatus.STATUS_SUCCESS)
        {
            throw new Exception($"Failed to create file on storage, NT_STATUS: {ntStatus}");
        }
        Console.WriteLine($"File \"{relativePath}\" has been created");

        try
        {
            var writeOffset = 0;
            var buffer = new byte[Math.Min(DefaultBufferSize, _smbClient.MaxWriteSize)];
            while (stream.Position < stream.Length)
            {
                var bytesRead = stream.Read(buffer, 0, buffer.Length);

                if (bytesRead != buffer.Length)
                {
                    Array.Resize(ref buffer, bytesRead);
                }

                ntStatus = _fileStore.WriteFile(out _, fileHandle, writeOffset, buffer);
                WriteIfInvalidSmbStatus(ntStatus);
                if (ntStatus != NTStatus.STATUS_SUCCESS)
                {
                    throw new Exception($"Failed to write to storage, NT_STATUS: {ntStatus}");
                }

                Console.WriteLine($"{bytesRead} bytes written to file \"{relativePath}\"");
                writeOffset += bytesRead;
            }
        }
        finally
        {
            if (fileHandle != null)
                _fileStore.CloseFile(fileHandle);
        }

        Console.WriteLine($"File \"{relativePath}\" has been send");
    }


    private static void WriteIfInvalidSmbStatus(NTStatus status)
    {
        if (status == NTStatus.STATUS_INVALID_SMB)
            Console.WriteLine("Request timeout? Connection lost?");
    }

    [MemberNotNull(nameof(_fileStore))]
    private static void Connect()
    {
        if (_smbClient.IsConnected && _fileStore != null)
        {
            return;
        }

        _smbClient = new SMB2Client();
        var serverName = _smbPath.ServerName;
        var isIpAddressSpecified = IPAddress.TryParse(serverName, out var ipAddress);

        var isConnected = isIpAddressSpecified
            ? _smbClient.Connect(ipAddress, SMBTransportType.DirectTCPTransport)
            : _smbClient.Connect(serverName, SMBTransportType.DirectTCPTransport);

        if (!isConnected)
        {
            isConnected = isIpAddressSpecified
                ? _smbClient.Connect(ipAddress, SMBTransportType.NetBiosOverTCP)
                : _smbClient.Connect(serverName, SMBTransportType.NetBiosOverTCP);

            if (!isConnected)
            {
                throw new Exception($"Can not connect to server {serverName}");
            }
        }

        var status = _smbClient.Login(_settings.Domain, _settings.Login, _settings.Password);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new Exception($"Can not login to server as {_settings.Domain}\\{_settings.Login}, NT_STATUS: {status}");
        }

        var shareName = _smbPath.ShareName;
        _fileStore = _smbClient.TreeConnect(shareName, out status);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            throw new Exception($"Can not connect to share \"{shareName}\", NT_STATUS: {status}");
        }
    }
}